const {contextBridge,ipcRenderer}=require("electron");
contextBridge.exposeInMainWorld("hub",{
  action:command=>ipcRenderer.send("action",command),
  readings:callback=>ipcRenderer.on("readings",(_,data)=>callback(data)),
  navigate:index=>ipcRenderer.send("navigate",index),
  onState:callback=>ipcRenderer.on("state",(_,state)=>callback(state))
});
