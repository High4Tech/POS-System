import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {generateKeyPairSync,verify,createHash} from 'node:crypto';

test('release signer binds exact package bytes, rejects extra fields and never overwrites output',async()=>{
 const folder=await mkdtemp(join(tmpdir(),'pos-release-contract-'));
 try{
  const {privateKey,publicKey}=generateKeyPairSync('ec',{namedCurve:'prime256v1'});
  const key=join(folder,'private.pem'),metadata=join(folder,'metadata.json'),packageFile=join(folder,'SupermarketPos-1.0.1-full.nupkg'),output=join(folder,'release.signed.json');
  await writeFile(key,privateKey.export({type:'pkcs8',format:'pem'}),{mode:0o600});const bytes=Buffer.from('Inert unsigned local contract test');await writeFile(packageFile,bytes);
  const data={schema:1,product:'supermarketpos',edition:'retail',channel:'testing',version:'1.0.1',minimumVersion:'1.0.0',minimumSchema:1,maximumSchema:1,kind:'bugfix',publishedAt:'2026-11-19T00:00:00Z',publisherThumbprint:'a'.repeat(40),notes:'Test notes'};
  await writeFile(metadata,JSON.stringify(data));
  const tool=fileURLToPath(new URL('../tools/sign-release.mjs',import.meta.url));
  const run=out=>execFileSync(process.execPath,[tool,metadata,packageFile,key,out],{stdio:'pipe'});
  run(output);const envelope=JSON.parse(await readFile(output,'utf8')),payload=Buffer.from(envelope.payload,'base64'),claims=JSON.parse(payload);
  assert.ok(verify('sha256',payload,{key:publicKey,dsaEncoding:'ieee-p1363'},Buffer.from(envelope.signature,'base64')));
  assert.equal(claims.size,bytes.length);assert.equal(claims.sha256,createHash('sha256').update(bytes).digest('hex'));assert.equal(claims.notes,'Test notes');
  assert.throws(()=>run(output));await writeFile(metadata,JSON.stringify({...data,unreviewedField:true}));assert.throws(()=>run(join(folder,'rejected.json')));
 }finally{await rm(folder,{recursive:true,force:true});}
});
