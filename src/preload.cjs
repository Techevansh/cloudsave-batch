const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("cloudsave", {
  selectFolder: () => ipcRenderer.invoke("select-folder"),
  scanFolder: (folderPath) => ipcRenderer.invoke("scan-folder", folderPath),
});
