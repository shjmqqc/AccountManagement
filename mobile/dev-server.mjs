// Local-only static host and same-origin API proxy. No dependencies, never used in production.
import http from 'node:http';
import {readFile} from 'node:fs/promises';
const files = {'/': ['index.html','text/html'], '/index.html':['index.html','text/html'], '/app.js':['app.js','text/javascript'], '/styles.css':['styles.css','text/css']};
http.createServer(async (req,res) => {
  if (req.url.startsWith('/accmgr-api/')) {
    const upstream = http.request({hostname:'127.0.0.1',port:18083,path:req.url.slice('/accmgr-api'.length),method:req.method,headers:req.headers}, response => {
      res.writeHead(response.statusCode,response.headers); response.pipe(res);
    });
    upstream.on('error', () => { res.writeHead(502); res.end('{"error":"upstream_unavailable"}'); }); req.pipe(upstream); return;
  }
  const file = files[req.url.split('?')[0]];
  if (!file) {res.writeHead(404);res.end();return;}
  try {res.writeHead(200,{'Content-Type':file[1]+'; charset=utf-8','Cache-Control':'no-store'});res.end(await readFile(new URL(file[0],import.meta.url)));}
  catch {res.writeHead(500);res.end();}
}).listen(18084,'127.0.0.1',() => console.log('Mobile preview http://127.0.0.1:18084'));
