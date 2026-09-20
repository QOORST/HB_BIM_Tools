import {pathToFileURL} from 'node:url';
const root=process.argv[2];
const sdk=root+'/node_modules/@modelcontextprotocol/sdk/dist/esm';
const {Server}=await import(pathToFileURL(sdk+'/server/index.js'));
const {StdioServerTransport}=await import(pathToFileURL(sdk+'/server/stdio.js'));
const {ListToolsRequestSchema,CallToolRequestSchema}=await import(pathToFileURL(sdk+'/types.js'));
const {RevitSocketClient}=await import(pathToFileURL(root+'/build/socket.js'));
const socket=new RevitSocketClient();
const server=new Server({name:'hb-bim-diagnostics',version:'1.0.0'},{capabilities:{tools:{}}});
server.setRequestHandler(ListToolsRequestSchema,async()=>({tools:[{
 name:'hb_diagnose_grid_dimensions',description:'Trial-run HB_BIM grid dimensions in the active plan view, then roll back. Does not test transaction commit. Uses all visible straight grids and saved settings.',
 inputSchema:{type:'object',properties:{},additionalProperties:false}
}]}));
server.setRequestHandler(CallToolRequestSchema,async request=>{
 if(request.params.name!=='hb_diagnose_grid_dimensions') throw new Error('Unknown diagnostic tool');
 try {
  if(!socket.isConnected()) await socket.connect();
  const result=await socket.sendCommand(request.params.name,{});
  return {content:[{type:'text',text:JSON.stringify(result.data,null,2)}]};
 } catch(e) {return {isError:true,content:[{type:'text',text:e.message}]};}
});
await server.connect(new StdioServerTransport());
