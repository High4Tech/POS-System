import {sha,sign,verify,sameSecret} from './crypto.mjs';
import {adminHtml,adminJs} from './admin.mjs';
const now=()=>new Date().toISOString(),uuid=()=>crypto.randomUUID();
const json=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'content-type':'application/json','cache-control':'no-store','x-content-type-options':'nosniff'}});
function text(v,max=200){if(typeof v!=='string'||v.length>max||/[\x00-\x08\x0b\x0c\x0e-\x1f]/.test(v))throw Error('INPUT');return v.trim();}
async function body(r){
 if(Number(r.headers.get('content-length'))>65536||!r.body)throw Error('INPUT');
 const reader=r.body.getReader(),chunks=[];let size=0;
 try{while(true){const {done,value}=await reader.read();if(done)break;size+=value.byteLength;if(size>65536){await reader.cancel();throw Error('INPUT');}chunks.push(value);}}
 finally{reader.releaseLock();}
 const bytes=new Uint8Array(size);let offset=0;for(const chunk of chunks){bytes.set(chunk,offset);offset+=chunk.length;}
 return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes));
}
async function audit(db,action,target){await db.prepare('INSERT INTO audit VALUES(?,?,?,?,?)').bind(uuid(),'publisher-admin',action,target,now()).run();}
async function activation(env,key,device,label){
 key=text(key,128);device=text(device,64);label=text(label,100);
 if(!/^[0-9a-f]{64}$/.test(device)||key.length<24)throw Error('ACTIVATION');
 const license=await env.DB.prepare("SELECT * FROM licenses WHERE key_hash=? AND status='active'").bind(await sha(key)).first();
 if(!license)throw Error('ACTIVATION');
 let active=await env.DB.prepare('SELECT * FROM activations WHERE license_id=? AND device=?').bind(license.id,device).first();
 if(active?.status==='revoked')throw Error('ACTIVATION');
 if(!active){
  const id=uuid(),date=now();
  try {await env.DB.batch([
   env.DB.prepare("INSERT INTO activations(id,license_id,device,label,created,last_seen) VALUES(?,?,?,?,?,?) ON CONFLICT(license_id,device) DO NOTHING").bind(id,license.id,device,label,date,date),
   env.DB.prepare('INSERT INTO audit VALUES(?,?,?,?,?)').bind(uuid(),'activation','device.activated',license.id,date)
  ]);}catch{throw Error('ACTIVATION');}
  active=await env.DB.prepare('SELECT * FROM activations WHERE license_id=? AND device=?').bind(license.id,device).first();
 }
 if(active?.status!=='active')throw Error('ACTIVATION');
 return sign({schema:1,product:'supermarketpos',licenseId:license.id,activationId:active.id,device,edition:license.edition,major:license.major,updatesUntil:license.updates_until,issuedAt:active.created},env.SIGNING_PRIVATE_KEY);
}
async function authorizeCertificate(env,envelope){
 const c=await verify(envelope,env.SIGNING_PUBLIC_KEY);
 if(c.schema!==1||c.product!=='supermarketpos')throw Error('AUTH');
 const record=await env.DB.prepare("SELECT a.id,l.* FROM activations a JOIN licenses l ON l.id=a.license_id WHERE a.id=? AND a.device=? AND a.license_id=? AND a.status='active' AND l.status='active'").bind(c.activationId,c.device,c.licenseId).first();
 if(!record)throw Error('AUTH');return {c,record};
}
async function rate(request,env){
 const minute=Math.floor(Date.now()/60000),id=await sha((request.headers.get('cf-connecting-ip')||'local')+':'+minute);
 const row=await env.DB.prepare('INSERT INTO rate_limits VALUES(?,1,?) ON CONFLICT(id) DO UPDATE SET count=count+1 RETURNING count').bind(id,minute+2).first();
 await env.DB.prepare('DELETE FROM rate_limits WHERE expires<?').bind(minute).run();
 return row.count<=30;
}
function eligible(r,claims,channel,current){
 const ver=s=>/^\d+\.\d+\.\d+$/.test(s)?s.split('.').map(Number):null;
 const a=ver(r.version),b=ver(current),min=ver(r.minimumVersion); if(!a||!b||!min)return false;
 const cmp=(x,y)=>x[0]-y[0]||x[1]-y[1]||x[2]-y[2];
 return r.schema===1&&r.product==='supermarketpos'&&r.channel===channel&&r.edition===claims.edition&&a[0]<=claims.major&&cmp(a,b)>0&&cmp(b,min)>=0&&new Date(r.publishedAt)<=new Date(claims.updatesUntil);
}
export default {async fetch(request,env){
 try{
  const url=new URL(request.url),p=url.pathname,method=request.method;
  if(url.protocol!=='https:'&&env.ENVIRONMENT!=='development')return json({error:'HTTPS required'},400);
  if(p==='/health')return json({ok:true,environment:env.ENVIRONMENT});
  if(p==='/admin'||p==='/admin.js')return new Response(p==='/admin'?adminHtml:adminJs,{headers:{'content-type':p==='/admin'?'text/html;charset=utf-8':'text/javascript','cache-control':'no-store','content-security-policy':"default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",'x-content-type-options':'nosniff'}});
  if(!await rate(request,env))return json({error:'Try again later'},429);
  if(p.startsWith('/admin/')){
   if(!env.ADMIN_TOKEN||env.ADMIN_TOKEN.length<32||!await sameSecret(request.headers.get('authorization'),'Bearer '+env.ADMIN_TOKEN))return json({error:'Unauthorized'},401);
   if(p==='/admin/data'&&method==='GET'){
    const result={};for(const t of ['customers','licenses','activations','transfers','releases','reports','audit']){
     const version=text(url.searchParams.get('version')||'',40),category=text(url.searchParams.get('category')||'',100);
     const query=t==='reports'?env.DB.prepare("SELECT * FROM reports WHERE (?='' OR version=?) AND (?='' OR category=?) ORDER BY rowid DESC LIMIT 200").bind(version,version,category,category):env.DB.prepare(`SELECT ${t==='licenses'?'id,customer_id,edition,seats,major,updates_until,created,status,test':'*'} FROM ${t} ORDER BY rowid DESC LIMIT 200`);
     result[t]=(await query.all()).results;
    }
    return json(result);
   }
   const b=await body(request);
   if(p==='/admin/customers'&&method==='POST'){
    const id=uuid();await env.DB.prepare('INSERT INTO customers(id,business,contact,created) VALUES(?,?,?,?)').bind(id,text(b.business),text(b.contact||''),now()).run();await audit(env.DB,'customer.created',id);return json({id},201);
   }
   if(p==='/admin/licenses'&&method==='POST'){
    if(!Number.isInteger(b.seats)||b.seats<1||b.seats>100||!Number.isInteger(b.major)||b.major<1||!Number.isFinite(Date.parse(b.updatesUntil)))throw Error('INPUT');
    const id=uuid(),key='POS-'+[...crypto.getRandomValues(new Uint8Array(24))].map(n=>n.toString(16).padStart(2,'0')).join('');
    await env.DB.prepare('INSERT INTO licenses(id,customer_id,key_hash,edition,seats,major,updates_until,created,test) VALUES(?,?,?,?,?,?,?,?,?)').bind(id,text(b.customerId),await sha(key),text(b.edition||'retail',50),b.seats,b.major,new Date(b.updatesUntil).toISOString(),now(),b.test===false?0:1).run();await audit(env.DB,'license.created',id);return json({id,key},201);
   }
   if(p==='/admin/license'&&method==='POST'){
    if(!Number.isFinite(Date.parse(b.updatesUntil))||!['active','suspended'].includes(b.status))throw Error('INPUT');
    await env.DB.prepare('UPDATE licenses SET updates_until=?,status=? WHERE id=?').bind(new Date(b.updatesUntil).toISOString(),b.status,text(b.id)).run();await audit(env.DB,'license.updated',b.id);return json({ok:true});
   }
   if(p==='/admin/offline-activate'&&method==='POST')return json(await activation(env,text(b.key,128),b.device,b.label));
   if(p==='/admin/release-seat'&&method==='POST'){
    await env.DB.batch([env.DB.prepare("UPDATE activations SET status='revoked' WHERE id=?").bind(text(b.activationId)),env.DB.prepare("UPDATE transfers SET status='approved' WHERE activation_id=? AND status='pending'").bind(b.activationId),env.DB.prepare('INSERT INTO audit VALUES(?,?,?,?,?)').bind(uuid(),'publisher-admin','device.seat.released',b.activationId,now())]);return json({ok:true});
   }
   if(p==='/admin/releases'&&method==='POST'){
    const r=await verify(b.envelope,env.SIGNING_PUBLIC_KEY);
    if(r.schema!==1||r.product!=='supermarketpos'||!['stable','testing','development'].includes(r.channel)||r.fileName!==b.objectKey||!/^[-A-Za-z0-9_.]+-full\.nupkg$/.test(r.fileName)||!Number.isSafeInteger(r.size)||r.size<=0||r.size>2000000000||!/^[a-fA-F0-9]{64}$/.test(r.sha256)||!/^(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})$/.test(r.publisherThumbprint)||!/^\d+\.\d+\.\d+$/.test(r.version)||!/^\d+\.\d+\.\d+$/.test(r.minimumVersion)||!Number.isFinite(Date.parse(r.publishedAt))||!['security','bugfix','minor','major'].includes(r.kind)||typeof(r.notes||'')!=='string'||(r.notes||'').length>8000)throw Error('INPUT');
    const id=uuid();await env.DB.prepare('INSERT INTO releases VALUES(?,?,?,?,?,?,?)').bind(id,r.version,r.edition,r.channel,JSON.stringify(b.envelope),r.fileName,now()).run();await audit(env.DB,'release.published',id);return json({id},201);
   }
   if(p==='/admin/reports'&&method==='POST'){
    if(!['Open','Investigating','Resolved'].includes(b.status))throw Error('INPUT');
    await env.DB.prepare('UPDATE reports SET status=?,fixed_release=? WHERE id=?').bind(b.status,b.fixedRelease||null,text(b.id)).run();await audit(env.DB,'report.updated',b.id);return json({ok:true});
   }
   return json({error:'Not found'},404);
  }
  if(p==='/v1/activate'&&method==='POST'){const b=await body(request);return json(await activation(env,b.key,b.device,b.label));}
  if(method!=='POST')return json({error:'Not found'},404);
  const b=await body(request),{c,record}=await authorizeCertificate(env,b.certificate);
  if(p==='/v1/status'){await env.DB.prepare('UPDATE activations SET last_seen=? WHERE id=?').bind(now(),c.activationId).run();return json({valid:true,updatesUntil:record.updates_until});}
  if(p==='/v1/transfer-request'){
   await env.DB.prepare('INSERT OR IGNORE INTO transfers(id,activation_id,created) VALUES(?,?,?)').bind(uuid(),c.activationId,now()).run();return json({status:'pending'});
  }
  if(p==='/v1/updates'){
   if(!['stable','testing','development'].includes(b.channel))throw Error('INPUT');
   const rows=(await env.DB.prepare('SELECT * FROM releases WHERE edition=? AND channel=?').bind(c.edition,b.channel).all()).results;
   const results=[];for(const row of rows){const r=await verify(JSON.parse(row.envelope),env.SIGNING_PUBLIC_KEY);if(eligible(r,c,b.channel,text(b.current,40)))results.push({envelope:JSON.parse(row.envelope),version:r.version,id:row.id});}
   results.sort((a,b)=>b.version.localeCompare(a.version,undefined,{numeric:true}));return json(results[0]||null);
  }
  if(p==='/v1/download'){
   const row=await env.DB.prepare('SELECT * FROM releases WHERE id=?').bind(text(b.releaseId)).first();
   if(!row||!env.RELEASES)return json({error:'Release storage not configured'},503);
   const release=await verify(JSON.parse(row.envelope),env.SIGNING_PUBLIC_KEY);
   if(!eligible(release,c,text(b.channel,20),text(b.current,40)))throw Error('AUTH');
   const object=await env.RELEASES.get(row.object_key);if(!object)return json({error:'Unavailable'},404);
   return new Response(object.body,{headers:{'content-type':'application/octet-stream','cache-control':'private,no-store','x-content-type-options':'nosniff'}});
  }
  if(p==='/v1/reports'){
   // Explicit allow-list. No arbitrary attached files, databases, catalog or FBR fields accepted.
   const d=b.diagnostics;if(!d||Object.keys(d).some(k=>!['appVersion','os','category','module','frames','at'].includes(k))||b.consent!==true)throw Error('INPUT');
   const diagnostic={appVersion:text(d.appVersion,40),os:text(d.os,100),category:text(d.category,100),module:text(d.module,100),frames:text(d.frames,12000),at:text(d.at,50)};
   const id=uuid();await env.DB.prepare('INSERT INTO reports(id,activation_id,version,category,summary,diagnostics,created) VALUES(?,?,?,?,?,?,?)').bind(id,c.activationId,diagnostic.appVersion,diagnostic.category,text(b.description,2000),JSON.stringify(diagnostic),now()).run();return json({id},201);
  }
  return json({error:'Not found'},404);
 }catch(e){return json({error:['INPUT','SIGNATURE'].includes(e.message)?'Invalid request':'Request could not be completed'},400);}
}};
