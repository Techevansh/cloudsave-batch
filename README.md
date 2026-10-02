# CloudSave Batch / UI Agent

CloudSave Batch is being developed as a Windows UI automation agent for Cloudium-protected U: drive workflows.

## Current proof of concept: v0.5

The agent does not directly enumerate the protected U: drive with Node.js filesystem APIs.

Instead it:

1. Starts Windows File Explorer.
2. Activates the Explorer window.
3. Focuses the address bar with Ctrl+L.
4. Pastes the configured U: target path.
5. Sends Enter and lets Explorer perform the navigation.

Target currently configured for the proof of concept:

`U:\부서 폴더\501. 공공부문 클라우드 네이티브 전문기술지원\10. 기술지원 2팀\099. 2팀_[개인폴더]\1. 백승훈 책임(H)`

## Local test

After pulling the latest main branch:

```powershell
cd C:\cloudsave-batch
git pull
.\Run-CloudSave-Agent.bat
```

Expected result: File Explorer navigates to the configured target folder.

## Planned next stages

- Inspect visible Explorer items through Windows UI Automation.
- Identify visible PPTX/XLSX/XLS items.
- Open one Office document through the Explorer UI.
- Detect PowerPoint or Excel.
- Trigger the existing CloudSave / CloudSave Excel Add-in workflow.
- Record success/failure and move to the next document.

The original cloudsave and cloudsave-excel repositories remain independent from this agent.
