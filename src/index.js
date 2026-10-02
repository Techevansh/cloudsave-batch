import path from "node:path";
import { scanOfficeFiles } from "./scanner.js";

const targetPath = process.argv.slice(2).join(" ").trim();

if (!targetPath) {
  console.error("Usage: npm run scan -- <folder-path>");
  console.error('Example: npm run scan -- "D:\\CompanyFiles"');
  process.exitCode = 1;
} else {
  try {
    const result = await scanOfficeFiles(targetPath);

    console.log("\nCloudSave Batch v0.1");
    console.log(`Scanning: ${result.root}\n`);

    if (result.files.length === 0) {
      console.log("No supported Office files found.");
    } else {
      for (const file of result.files) {
        console.log(`[FILE] ${file.relativePath}  (${formatBytes(file.size)})`);
      }
    }

    console.log("\n----------------------------------------");
    console.log(`${result.summary.total} Office file(s) detected.`);
    console.log(`PPTX : ${result.summary.pptx}`);
    console.log(`XLSX : ${result.summary.xlsx}`);
    console.log(`XLS  : ${result.summary.xls}`);
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
