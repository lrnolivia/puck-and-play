cbuffer Frame : register(b0) { float4 Tint; float4 Lighting; };
Texture2D Studio : register(t0);
SamplerState StudioSampler : register(s0);
struct Vertex { float3 position:POSITION; float3 normal:NORMAL; };
struct Fragment { float4 screen:SV_Position; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
float3 display(float3 radiance) {
    float peak=max(radiance.r,max(radiance.g,radiance.b));
    float3 mapped=radiance/(1.+peak);
    return pow(saturate(mapped),1./2.2);
}
float3 environment(float3 direction) {
    // The same studio as the icon, rotated into the object's front view.
    float c=.9063078,s=.4226183;
    direction.xy=float2(c*direction.x-s*direction.y,s*direction.x+c*direction.y);
    float3 q=float3(direction.x,.3420201*direction.y-.9396926*direction.z,.9396926*direction.y+.3420201*direction.z);
    float2 uv=float2(atan2(q.y,q.x)/6.2831853+.5,asin(clamp(q.z,-1.,1.))/3.14159265+.5);
    return Studio.SampleLevel(StudioSampler,uv,0).rgb;
}
Fragment CrystalVS(Vertex input) {
    Fragment o;o.world=input.position;o.normal=input.normal;
    o.screen=float4(input.position.xy/Lighting.w,.5-input.position.z*.1,1.);return o;
}
float4 CrystalPS(Fragment input):SV_Target {
    float3 N=normalize(input.normal),V=float3(0,0,1);
    float nv=saturate(dot(N,V)),fresnel=.035+.965*pow(1.-nv,5.);
    float3 reflected=environment(reflect(-V,N));
    float3 refracted=environment(refract(-V,N,1./1.46));
    float3 absorption=exp(-(1.-Tint.rgb)*(.45+.22/max(.25,nv)));
    float2 q=(input.world.xy-float2(-.13,0.))*2.6;
    float core=exp(-dot(q,q)*1.85);
    float pulse=Lighting.x,lit=Lighting.y;
    float cleanYellow=step(.95,Tint.r)*step(.60,Tint.g)*step(Tint.b,.1);
    float baseRadiance=lerp(.16,.30,cleanYellow);
    float3 emission=Tint.rgb*lit*(baseRadiance+(1.-baseRadiance)*core)*(2.2+pulse*4.0);
    float3 transmission=refracted*.08*absorption*Tint.rgb;
    float3 softbox=reflected*fresnel*.18;
    float3 halfVector=normalize(normalize(float3(-1.8,4.5,7))+V);
    float spec=pow(saturate(dot(N,halfVector)),180.)*.3;
    float3 color=display(emission+transmission+softbox+spec);
    float alpha=saturate(.24+fresnel*.72+lit*(.24+.30*core));
    // The UI compositor consumes premultiplied BGRA. Pixels beyond the mesh
    // remain RGBA zero, so the real acrylic surface is visible around it.
    return float4(color*alpha,alpha);
}
Fragment GlowVS(uint id:SV_VertexID) {
    Fragment o;float2 p=float2((id<<1)&2,id&2);
    o.screen=float4(p*float2(2,-2)+float2(-1,1),.95,1);
    o.world=float3((p-float2(.5,.5))*float2(2.*Lighting.w,-2.*Lighting.w),0);o.normal=0;return o;
}
float crystalDistance(float2 p) {
    const float k=1.73205080757,r=k*(.49-.16875);
    float2 q=float2(abs(p.y)-r,p.x+.13+r/k);
    if(q.x+k*q.y>0.)q=float2(q.x-k*q.y,-k*q.x-q.y)*.5;
    q.x-=clamp(q.x,-2.*r,0.);
    return -length(q)*sign(q.y)-.16875;
}
float4 GlowPS(Fragment input):SV_Target {
    if(Lighting.z<.5)return 0;
    // Bloom starts at the actual rounded crystal silhouette. There is no
    // separate circular pool underneath the object.
    float signedDistance=crystalDistance(input.world.xy);
    float distanceToEdge=max(0.,signedDistance);
    // Follow the local edge back to its light source. Broad, directional
    // diffusion carries the core's energy into the halo without a uniform
    // outline, moving noise, or separate luminous blobs.
    float2 edgeNormal=normalize(float2(ddx(signedDistance),-ddy(signedDistance))+float2(.00001,0.));
    float2 edge=input.world.xy-edgeNormal*distanceToEdge;
    float2 sourceOffset=(edge-float2(-.25,.12))*float2(1.35,1.05);
    float sourceFocus=exp(-dot(sourceOffset,sourceOffset)*1.7);
    float lightFacing=saturate(dot(edgeNormal,normalize(float2(-.8,1.3)))*.5+.5);
    float diffusion=lerp(.55,1.18,sourceFocus)*lerp(.78,1.12,lightFacing);
    float spread=lerp(.76,1.0,sourceFocus)*lerp(.88,1.0,lightFacing);
    // All three layers breathe outward from the same silhouette. Changing
    // their reach, as well as their energy, makes the light expand and recede.
    float breath=saturate(Lighting.x);
    float innerWidth=lerp(.035,.060,breath);
    float nearWidth=lerp(.075,.160,breath)*lerp(.85,1.04,sourceFocus);
    float wideWidth=lerp(.160,.360,breath)*spread;
    float innerAura=exp(-distanceToEdge*distanceToEdge/(innerWidth*innerWidth));
    float nearGlow=exp(-distanceToEdge*distanceToEdge/(nearWidth*nearWidth));
    float wideGlow=exp(-distanceToEdge*distanceToEdge/(wideWidth*wideWidth));
    float opticalDepth=innerAura*.28*lerp(.74,1.12,sourceFocus)+nearGlow*.34*diffusion+wideGlow*.24*diffusion;
    float alpha=1.-exp(-opticalDepth*(.30+breath*.90)*lerp(1.,Lighting.y,.75));
    // The render surface includes transparent overscan. Finish the already
    // invisible tail by distance from the shape, never by a square viewport.
    alpha*=1.-smoothstep(.95,1.25,distanceToEdge);
    // A hotter inner aura transitions into saturated color and then clear
    // air. The layers add luminosity depth without a hard ring or grain.
    float3 hotColor=lerp(Tint.rgb,float3(1.,1.,1.),.28)*5.;
    float3 color=display(lerp(Tint.rgb*(1.9+sourceFocus*.65),hotColor,innerAura*(.40+sourceFocus*.30)));
    return float4(color*alpha,alpha);
}
