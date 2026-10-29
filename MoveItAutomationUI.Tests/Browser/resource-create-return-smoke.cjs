// Execute both native-preference creation handlers: a successful POST closes New and refreshes its caller.
'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const read=file=>fs.readFileSync(path.resolve(__dirname,'../..',file),'utf8');
function productionFunction(source,name){const start=source.indexOf('    async function '+name+'(');assert.ok(start>=0);const end=source.indexOf('\n    }',start)+6;return source.slice(start,end);}
(async()=>{
 let checks=0;
 for(const [file,kind,title]of [['scripts','Script','Script'],['datelists','DateList','Date List']]){
  const source=read('wwwroot/js/'+file+'.js');
  for(const outcome of ['success','rejected','uncertain']){
   let closed=0,refreshed=0,requests=0,error='';const payloads=[];
   const context={window:{MauiNativeEditor:{reserve(){throw new Error('New must not open an editor.');}}},endpoint:key=>key,
    fetchJson:async(_url,options)=>{requests++;payloads.push(JSON.parse(options.body));if(outcome!=='success'){const failure=new Error('Creation failed');if(outcome==='rejected')failure.status=400;throw failure;}return {success:true,script:{id:'42',name:'Example'},dateList:{id:'42',name:'Example'}};},showStatus:()=>{}};
   Object.assign(context,{['new'+kind+'Busy']:false,['new'+kind+'Uncertain']:false,['new'+kind+'Name']:{value:'Example',focus(){}},
    ['new'+kind+'Dialog']:{querySelector:()=>({value:kind==='Script'?'PS1':'Description'}),close:()=>closed++},
    ['showNew'+kind+'Error']:message=>error=message,['hideNew'+kind+'Error']:()=>error='',
    ['setNew'+kind+'Busy']:value=>context['new'+kind+'Busy']=value,
    ['refresh'+kind+'Grid']:async()=>refreshed++});
   vm.createContext(context);vm.runInContext(productionFunction(source,'createNew'+kind),context);
   await vm.runInContext('createNew'+kind+'()',context);
   assert.equal(closed,outcome==='success'?1:0);checks++;
   assert.equal(refreshed,outcome==='success'?1:0);checks++;
   assert.equal(context['new'+kind+'Busy'],false);checks++;
   assert.equal(context['new'+kind+'Uncertain'],outcome==='uncertain');checks++;
   assert.deepEqual(payloads,[kind==='Script'?{name:'Example',language:'PS1',resourceGroup:''}:{name:'Example',description:'Description'}]);checks++;
   if(outcome==='uncertain'){await vm.runInContext('createNew'+kind+'()',context);assert.equal(requests,1);checks++;}
   if(outcome!=='success'){assert.match(error,/Creation failed/);checks++;}
  }
 }
 console.log('PASS '+checks+' Script/Date List create-and-return, error and no-replay checks.');
})().catch(error=>{console.error(error);process.exitCode=1;});
