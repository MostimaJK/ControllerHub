if (!window.hub && window.chrome?.webview) {
  const bridge=window.chrome.webview;
  const listen=(topic,callback)=>bridge.addEventListener("message",event=>{
    if(event.data.topic===topic)callback(event.data.data);
  });
  window.hub={
    navigate:index=>bridge.postMessage("page "+index),
    action:command=>bridge.postMessage(JSON.stringify(command)),
    onState:callback=>listen("HUB",callback),
    readings:callback=>listen("LIVE",callback)
  };
  window.addEventListener("DOMContentLoaded",()=>bridge.postMessage("ready"));
}
