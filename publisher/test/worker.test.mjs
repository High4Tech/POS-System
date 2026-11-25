import test,{after,before} from 'node:test';
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {getPlatformProxy} from 'wrangler';
import worker from '../src/worker.mjs';
import {b64,sign,verify,sha} from '../src/crypto.mjs';
let proxy,env,customer,license,cert;
const token='test-admin-'+crypto.randomUUID();
async function api(path,data,admin=false,headers={}){
 const request=new Request('http://localhost'+path,{method:data?'POST':'GET',headers:{'content-type':'application/json','cf-connecting-ip':crypto.randomUUID(),...(admin?{authorization:'Bearer '+token}:{}),...headers},body:data?JSON.stringify(data):undefined});
 const response=await worker.fetch(request,env);return {status:response.status,data:await response.json()};
}
before(async()=>{
 proxy=await getPlatformProxy({configPath:'wrangler.jsonc',persist:false});
 const sql=await readFile(new URL('../migrations/0001_publisher.sql',import.meta.url),'utf8');
 // D1 exec accepts a SQL string; keep each trigger together for the D1 line-oriented exec implementation.
 for(const statement of sql.split(/;\s*(?=(?:CREATE|PRAGMA)\b|$)/).map(s=>s.trim()).filter(Boolean))await proxy.env.DB.prepare(statement).run();
 const key=await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},true,['sign','verify']);
 const pem=async(type,key,format)=>'-----BEGIN '+type+'-----\n'+b64(await crypto.subtle.exportKey(format,key))+'\n-----END '+type+'-----';
 env={...proxy.env,ENVIRONMENT:'development',ADMIN_TOKEN:token,SIGNING_PRIVATE_KEY:await pem('PRIVATE KEY',key.privateKey,'pkcs8'),SIGNING_PUBLIC_KEY:await pem('PUBLIC KEY',key.publicKey,'spki')};
});
after(async()=>{
 if(cert){await mkdir(new URL('../../engineering/qa-data/',import.meta.url),{recursive:true});await writeFile(new URL('../../engineering/qa-data/worker-signed-fixture.json',import.meta.url),JSON.stringify({publicKeyPem:env.SIGNING_PUBLIC_KEY,certificate:cert,device:'c'.repeat(64)}));}
 await proxy?.dispose();
});
test('admin authentication is enforced and license keys are returned only at creation',async()=>{
 assert.equal((await api('/admin/data')).status,401);
 customer=(await api('/admin/customers',{business:'Test shop',contact:''},true)).data;
 const issued=await api('/admin/licenses',{customerId:customer.id,seats:1,major:1,edition:'retail',updatesUntil:'2030-01-01T00:00:00Z'},true);
 assert.equal(issued.status,201);license=issued.data;assert.match(license.key,/^POS-/);
 const data=(await api('/admin/data',null,true)).data;
 assert.ok(!('key_hash' in data.licenses[0]));assert.ok(!JSON.stringify(data).includes(license.key));
});
test('concurrent activations enforce seat limit; retries preserve activation identity',async()=>{
 const responses=await Promise.all(['a','b'].map(char=>api('/v1/activate',{key:license.key,device:char.repeat(64),label:'test'})));
 assert.equal(responses.filter(r=>r.status===200).length,1);
 cert=responses.find(r=>r.status===200).data;
 const claims=await verify(cert,env.SIGNING_PUBLIC_KEY);
 assert.equal(claims.major,1);assert.equal(claims.product,'supermarketpos');
 const retry=await api('/v1/activate',{key:license.key,device:claims.device,label:'renamed'});
 assert.equal((await verify(retry.data,env.SIGNING_PUBLIC_KEY)).activationId,claims.activationId);
 const tampered={...cert,payload:cert.payload.slice(0,-4)+'AAAA'};
 assert.equal((await api('/v1/status',{certificate:tampered})).status,400);
});
test('offline activation, transfer requests and authorized seat release',async()=>{
 const claims=await verify(cert,env.SIGNING_PUBLIC_KEY);
 assert.equal((await api('/v1/transfer-request',{certificate:cert})).data.status,'pending');
 assert.equal((await api('/admin/release-seat',{activationId:claims.activationId})).status,401);
 assert.equal((await api('/admin/release-seat',{activationId:claims.activationId},true)).status,200);
 assert.equal((await api('/v1/activate',{key:license.key,device:claims.device,label:'old'})).status,400);
 const offline=await api('/admin/offline-activate',{key:license.key,device:'c'.repeat(64),label:'replacement'},true);
 assert.equal(offline.status,200);cert=offline.data;assert.equal((await verify(cert,env.SIGNING_PUBLIC_KEY)).device,'c'.repeat(64));
});
test('signed releases, version/channel and update entitlement filtering',async()=>{
 const content='A'.repeat(100);
 const release={schema:1,product:'supermarketpos',edition:'retail',channel:'stable',version:'1.0.1',minimumVersion:'1.0.0',minimumSchema:1,maximumSchema:1,kind:'bugfix',publishedAt:'2026-11-19T00:00:00Z',fileName:'SupermarketPos-1.0.1-full.nupkg',size:100,sha256:await sha(content),publisherThumbprint:'b'.repeat(40),notes:'Local testing only'};
 const envelope=await sign(release,env.SIGNING_PRIVATE_KEY);
 assert.equal((await api('/admin/releases',{envelope,objectKey:release.fileName},true)).status,201);
 assert.equal((await api('/v1/updates',{certificate:cert,channel:'stable',current:'1.0.0'})).data.version,'1.0.1');
 assert.equal((await api('/v1/updates',{certificate:cert,channel:'testing',current:'1.0.0'})).data,null);
 assert.equal((await api('/v1/updates',{certificate:cert,channel:'stable',current:'1.0.1'})).data,null);
 assert.equal((await api('/v1/download',{certificate:cert,releaseId:'invalid',channel:'stable',current:'1.0.0'})).status,503);
 await env.RELEASES.put(release.fileName,content);
 const eligible=(await api('/v1/updates',{certificate:cert,channel:'stable',current:'1.0.0'})).data;
 const download=async certificate=>worker.fetch(new Request('http://localhost/v1/download',{method:'POST',headers:{'content-type':'application/json','cf-connecting-ip':crypto.randomUUID()},body:JSON.stringify({certificate,releaseId:eligible.id,channel:'stable',current:'1.0.0'})}),env);
 const downloaded=await download(cert);assert.equal(downloaded.status,200);assert.equal(await downloaded.text(),content);
 const oldClaims={...await verify(cert,env.SIGNING_PUBLIC_KEY),updatesUntil:'2020-01-01T00:00:00Z'};
 const old=await sign(oldClaims,env.SIGNING_PRIVATE_KEY);assert.equal((await download(old)).status,400);
 assert.equal((await api('/v1/updates',{certificate:old,channel:'stable',current:'1.0.0'})).data,null);
});
test('diagnostic submissions require explicit consent and reject extra data fields',async()=>{
 const diagnostics={appVersion:'1.0.0',os:'Windows',category:'Test',module:'test',frames:'Module.Method',at:new Date().toISOString()};
 assert.equal((await api('/v1/reports',{certificate:cert,diagnostics,description:'test',consent:false})).status,400);
 assert.equal((await api('/v1/reports',{certificate:cert,diagnostics:{...diagnostics,invoice:'private'},description:'test',consent:true})).status,400);
 const report=await api('/v1/reports',{certificate:cert,diagnostics,description:'test',consent:true});assert.equal(report.status,201);
 assert.equal((await api('/admin/reports',{id:report.data.id,status:'Resolved'},true)).status,200);
 const data=(await api('/admin/data',null,true)).data;assert.equal(data.reports[0].status,'Resolved');assert.ok(data.audit.length>0);
 assert.equal((await api('/admin/data?version=none&category=Test',null,true)).data.reports.length,0);
});
test('activation abuse is rate limited and HTTPS is required outside development',async()=>{
 let last;for(let i=0;i<31;i++)last=await api('/v1/activate',{key:'invalid-key',device:'d'.repeat(64),label:'test'},false,{'cf-connecting-ip':'fixed-abuse-source'});
 assert.equal(last.status,429);
 const r=await worker.fetch(new Request('http://localhost/health'),{...env,ENVIRONMENT:'production'});assert.equal(r.status,400);
});
