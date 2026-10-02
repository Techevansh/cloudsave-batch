import { scanOfficeFiles } from "./scanner.js";

const targetPath = process.argv.slice(2).join(" ").trim();

if (!targetPath) {
  console.error("Usage: npm run scan -- <folder-path>");
  console.error('Example: npm run scan -- "C:\\cloudsave-test"');
  process.exitCode = 1;
} else {
  try {
    const result = await scanOfficeFiles(targetPath);

    console.log("\nCloudSave Batch v0.2");
    console.log(`Scanning: ${result.root}\n`);

    for (const folder of result.folders) {
      console.log(`[DIR ] ${folder.relativePath}`);
    }

    for (const file of result.files) {
      console.log(`[FILE] ${file.relativePath}  (${formatBytes(file.size)})`);
    }

    if (result.folders.length === 0 && result.files.length === 0) {
      console.log("No folders or supported Office files found.");
    }

    console.log("\n----------------------------------------");
    console.log(`Folders      : ${result.summary.folders}`);
    console.log(`Office files : ${result.summary.total}`);
    console.log(`PPTX         : ${result.summary.pptx}`);
    console.log(`XLSX         : ${result.summary.xlsx}`);
    console.log(`XLS          : ${result.summary.xls}`);
    console.log("----------------------------------------");
    console.log("Scan complete.\n");
  } catch (error) {
    console.error(`CloudSave scan failed: ${error.message}`);
    process.exitCode = 1;
  }
}

function formatBytes(bytes) {
  if (bytes === 0) return "0 B";

  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(
    Math.floor(Math.log(bytes) / Math.log(1024)),
    units.length - 1,
  );
  const value = bytes / 1024 ** index;

  return `${value.toFixed(index === 0 ? 0 : 2)} ${units[index]}`;
}
