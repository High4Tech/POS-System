// Generates test secrets locally, never in the Windows app or committed source.
import {mkdir,writeFile} from 'node:fs/promises';
import {b64} from '../src/crypto.mjs';
const pair=await crypto.subtle.generateKey({name:'ECDSA',namedCurve:'P-256'},true,['sign','verify']);
const pem=async(type,key,format)=>'-----BEGIN '+type+'-----\n'+b64(await crypto.subtle.exportKey(format,key))+'\n-----END '+type+'-----';
const privateKey=await pem('PRIVATE KEY',pair.privateKey,'pkcs8'),publicKey=await pem('PUBLIC KEY',pair.publicKey,'spki');
await mkdir('local-secrets',{recursive:true});
await writeFile('local-secrets/public.pem',publicKey,{flag:'wx'});
await writeFile('local-secrets/private.pem',privateKey,{flag:'wx',mode:0o600});
const secret='ADMIN_TOKEN='+JSON.stringify('local-test-'+crypto.randomUUID())+'\nSIGNING_PRIVATE_KEY='+JSON.stringify(privateKey)+'\nSIGNING_PUBLIC_KEY='+JSON.stringify(publicKey)+'\n';
await writeFile('.dev.vars',secret,{flag:'wx',mode:0o600});
console.log('Created ignored local test secrets. Read the local administrator token from .dev.vars. Never reuse these keys in production.');
