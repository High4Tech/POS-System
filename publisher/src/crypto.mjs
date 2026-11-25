export const enc=new TextEncoder();
export const b64=bytes=>btoa(String.fromCharCode(...new Uint8Array(bytes)));
export const unb64=s=>Uint8Array.from(atob(s),c=>c.charCodeAt(0));
export async function sha(value){return [...new Uint8Array(await crypto.subtle.digest('SHA-256',enc.encode(value)))].map(b=>b.toString(16).padStart(2,'0')).join('');}
function pemBytes(pem){return unb64(pem.replace(/-----[^-]+-----/g,'').replace(/\s/g,''));}
export async function sign(data,pem){const key=await crypto.subtle.importKey('pkcs8',pemBytes(pem),{name:'ECDSA',namedCurve:'P-256'},false,['sign']);const bytes=enc.encode(JSON.stringify(data));return {payload:b64(bytes),signature:b64(await crypto.subtle.sign({name:'ECDSA',hash:'SHA-256'},key,bytes))};}
export async function verify(envelope,pem){
 if(!envelope||typeof envelope.payload!=='string'||envelope.payload.length>44000||typeof envelope.signature!=='string')throw Error('SIGNATURE');
 const key=await crypto.subtle.importKey('spki',pemBytes(pem),{name:'ECDSA',namedCurve:'P-256'},false,['verify']);
 if(!await crypto.subtle.verify({name:'ECDSA',hash:'SHA-256'},key,unb64(envelope.signature),unb64(envelope.payload)))throw Error('SIGNATURE');
 return JSON.parse(new TextDecoder().decode(unb64(envelope.payload)));
}
export async function sameSecret(a,b){if(!a||!b)return false;const aa=await sha(a),bb=await sha(b);let d=0;for(let i=0;i<aa.length;i++)d|=aa.charCodeAt(i)^bb.charCodeAt(i);return d===0;}
