// CPU depth-buffer rendering of the actual evaluated GLB triangles.
// Input: eight float32 values per vertex (position, normal, UV), uint32 indices.
const fs = require('fs');
const [jobPath] = process.argv.slice(2);
const job = JSON.parse(fs.readFileSync(jobPath, 'utf8'));
const meshBytes = fs.readFileSync(job.vertices);
const vertices = new Float32Array(meshBytes.buffer, meshBytes.byteOffset, meshBytes.byteLength / 4);
const indexBytes = fs.readFileSync(job.indices);
const indices = new Uint32Array(indexBytes.buffer, indexBytes.byteOffset, indexBytes.byteLength / 4);
const textures = {};
for (const [id,spec] of Object.entries(job.textures)) textures[id]={...spec,bytes:fs.readFileSync(spec.path)};
const width = job.size, height = job.size;
const color = Buffer.alloc(width * height * 4);
const depth = new Float32Array(width * height); depth.fill(Infinity);
const visiblePart = new Uint16Array(width*height);visiblePart.fill(65535);
function normalize(a) { const l = Math.hypot(...a); return a.map(v => v / l); }
function cross(a, b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]; }
const forward = normalize(job.target.map((v,i) => v-job.camera[i]));
const right = normalize(cross(forward,[0,1,0]));
const up = cross(right,forward);
const light = normalize([-0.5,0.75,0.65]);
const count = vertices.length / 8;
const screen = new Float32Array(count*3);
for (let i=0;i<count;i++) {
  const offset=i*8, delta=[vertices[offset]-job.target[0],vertices[offset+1]-job.target[1],vertices[offset+2]-job.target[2]];
  screen[i*3]=width*.5+dot(delta,right)*width/job.span;
  screen[i*3+1]=height*.5-dot(delta,up)*height/job.span;
  screen[i*3+2]=dot(delta,forward);
}
for (let partId=0;partId<job.parts.length;partId++) {
  const part=job.parts[partId];
  for (let at=part.start;at<part.end;at+=3) {
    const a=indices[at],b=indices[at+1],c=indices[at+2];
    const ax=screen[a*3],ay=screen[a*3+1],bx=screen[b*3],by=screen[b*3+1],cx=screen[c*3],cy=screen[c*3+1];
    const denominator=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);
    if (Math.abs(denominator)<1e-6) continue;
    const x0=Math.max(0,Math.floor(Math.min(ax,bx,cx))),x1=Math.min(width-1,Math.ceil(Math.max(ax,bx,cx)));
    const y0=Math.max(0,Math.floor(Math.min(ay,by,cy))),y1=Math.min(height-1,Math.ceil(Math.max(ay,by,cy)));
    const ai=a*8,bi=b*8,ci=c*8;
    for (let y=y0;y<=y1;y++) for(let x=x0;x<=x1;x++) {
      const wa=((by-cy)*(x+.5-cx)+(cx-bx)*(y+.5-cy))/denominator;
      const wb=((cy-ay)*(x+.5-cx)+(ax-cx)*(y+.5-cy))/denominator;
      const wc=1-wa-wb;
      if(wa<-.0001||wb<-.0001||wc<-.0001) continue;
      const z=wa*screen[a*3+2]+wb*screen[b*3+2]+wc*screen[c*3+2];
      const pixel=y*width+x;
      if(z>=depth[pixel]) continue;
      depth[pixel]=z;
      visiblePart[pixel]=partId;
      let rgb=part.color;
      if(part.textured) {
        const textureSpec=textures[part.texture],texture=textureSpec.bytes;
        const u=Math.min(1,Math.max(0,wa*vertices[ai+6]+wb*vertices[bi+6]+wc*vertices[ci+6]));
        const v=Math.min(1,Math.max(0,wa*vertices[ai+7]+wb*vertices[bi+7]+wc*vertices[ci+7]));
        const tx=Math.min(textureSpec.width-1,Math.floor(u*textureSpec.width)),ty=Math.min(textureSpec.height-1,Math.floor(v*textureSpec.height));
        const p=(ty*textureSpec.width+tx)*4;
        rgb=[texture[p]/255,texture[p+1]/255,texture[p+2]/255];
      }
      let nx=wa*vertices[ai+3]+wb*vertices[bi+3]+wc*vertices[ci+3],ny=wa*vertices[ai+4]+wb*vertices[bi+4]+wc*vertices[ci+4],nz=wa*vertices[ai+5]+wb*vertices[bi+5]+wc*vertices[ci+5];
      const length=Math.hypot(nx,ny,nz)||1; nx/=length;ny/=length;nz/=length;
      const illumination=.77+.23*Math.max(0,nx*light[0]+ny*light[1]+nz*light[2]);
      const p=pixel*4;
      color[p]=Math.min(255,Math.round(255*rgb[0]*illumination));
      color[p+1]=Math.min(255,Math.round(255*rgb[1]*illumination));
      color[p+2]=Math.min(255,Math.round(255*rgb[2]*illumination));
      color[p+3]=255;
    }
  }
}
fs.writeFileSync(job.output,color);
fs.writeFileSync(job.output+'.parts.bin',Buffer.from(visiblePart.buffer));
