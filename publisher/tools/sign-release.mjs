import {readFile,writeFile} from 'node:fs/promises';
import {createPrivateKey,createPublicKey,createHash,sign,verify} from 'node:crypto';
import {basename,dirname,join} from 'node:path';

// Private keys are file input on the publisher machine, never a desktop build property or CLI value.
const [metadataFile,packageFile,privateKeyFile,outputFile]=process.argv.slice(2);
if(!outputFile)throw Error('Usage: node tools/sign-release.mjs metadata.json package-full.nupkg private.pem release.signed.json');
const metadata=JSON.parse(await readFile(metadataFile,'utf8'));
const allowed=['schema','product','edition','channel','version','minimumVersion','minimumSchema','maximumSchema','kind','publishedAt','publisherThumbprint','notes'];
if(Object.keys(metadata).some(k=>!allowed.includes(k)))throw Error('Unexpected release field');
const key=createPrivateKey(await readFile(privateKeyFile,'utf8'));
if(key.asymmetricKeyType!=='ec'||key.asymmetricKeyDetails.namedCurve!=='prime256v1')throw Error('Use a dedicated P-256 signing key');
if(metadata.product!=='supermarketpos'||metadata.schema!==1||!['stable','testing','development'].includes(metadata.channel)||!/^\d+\.\d+\.\d+$/.test(metadata.version)||!/^\d+\.\d+\.\d+$/.test(metadata.minimumVersion)||!['security','bugfix','minor','major'].includes(metadata.kind)||!Number.isFinite(Date.parse(metadata.publishedAt))||!Number.isInteger(metadata.minimumSchema)||!Number.isInteger(metadata.maximumSchema)||!/^(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})$/.test(metadata.publisherThumbprint)||(metadata.notes||'').length>8000)throw Error('Invalid release metadata');
const bytes=await readFile(packageFile);
const claims={...metadata,fileName:basename(packageFile),size:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')};
if(!/^[-A-Za-z0-9_.]+-full\.nupkg$/.test(claims.fileName))throw Error('Choose the Velopack full package');
const payload=Buffer.from(JSON.stringify(claims)),signature=sign('sha256',payload,{key,dsaEncoding:'ieee-p1363'});
if(!verify('sha256',payload,{key:createPublicKey(key),dsaEncoding:'ieee-p1363'},signature))throw Error('Signature self-check failed');
await writeFile(outputFile,JSON.stringify({payload:payload.toString('base64'),signature:signature.toString('base64')},null,2),{flag:'wx'});
console.log('Signed release metadata written. Review it before publishing; nothing was uploaded.');
