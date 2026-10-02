const folderPath = document.getElementById("folderPath");
const selectFolderButton = document.getElementById("selectFolder");
const scanFolderButton = document.getElementById("scanFolder");
const message = document.getElementById("message");
const results = document.getElementById("results");
const summary = document.getElementById("summary");
const rootLabel = document.getElementById("rootLabel");

function updateScanButton() {
  scanFolderButton.disabled = folderPath.value.trim().length === 0;
}

folderPath.addEventListener("input", () => {
  updateScanButton();
  message.textContent = folderPath.value.trim()
    ? "입력한 경로를 구조 판독할 수 있습니다."
    : "폴더를 선택하거나 U:\\ 같은 경로를 직접 입력하세요.";
});

folderPath.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !scanFolderButton.disabled) scanFolderButton.click();
});

selectFolderButton.addEventListener("click", async () => {
  const selectedPath = await window.cloudsave.selectFolder();
  if (!selectedPath) return;
  folderPath.value = selectedPath;
  updateScanButton();
  message.textContent = "폴더가 선택되었습니다. 구조 판독을 시작할 수 있습니다.";
});

scanFolderButton.addEventListener("click", async () => {
  const target = folderPath.value.trim();
  if (!target) return;
  scanFolderButton.disabled = true;
  selectFolderButton.disabled = true;
  folderPath.disabled = true;
  message.textContent = "폴더 구조를 판독하고 있습니다...";
  results.className = "results";
  results.textContent = "Scanning...";
  try {
    const result = await window.cloudsave.scanFolder(target);
    renderResult(result);
    message.textContent = "구조 판독이 완료되었습니다.";
  } catch (error) {
    results.textContent = `판독 실패: ${error.message}`;
    message.textContent = "경로를 확인하세요. 예: U:\\ 또는 U:\\공유 폴더";
  } finally {
    folderPath.disabled = false;
    selectFolderButton.disabled = false;
    updateScanButton();
  }
});

function renderResult(result) {
  document.getElementById("folderCount").textContent = result.summary.folders;
  document.getElementById("fileCount").textContent = result.summary.total;
  document.getElementById("pptxCount").textContent = result.summary.pptx;
  document.getElementById("xlsxCount").textContent = result.summary.xlsx;
  document.getElementById("xlsCount").textContent = result.summary.xls;
  summary.classList.remove("hidden");
  rootLabel.textContent = result.root;
  results.replaceChildren();
  if (result.folders.length === 0 && result.files.length === 0) {
    results.classList.add("empty");
    results.textContent = "판독할 폴더 또는 지원되는 Office 파일이 없습니다.";
    return;
  }
  for (const folder of result.folders) appendLine("DIR ", folder.relativePath, "dir");
  for (const file of result.files) appendLine("FILE", `${file.relativePath}  (${formatBytes(file.size)})`, "file");
}

function appendLine(type, text, className) {
  const line = document.createElement("div");
  line.className = `result-line ${className}`;
  line.textContent = `[${type}] ${text}`;
  results.appendChild(line);
}

function formatBytes(bytes) {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / 1024 ** index;
  return `${value.toFixed(index === 0 ? 0 : 2)} ${units[index]}`;
}

updateScanButton();
