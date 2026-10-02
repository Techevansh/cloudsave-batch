import { readdir, stat } from "node:fs/promises";
import path from "node:path";

export const SUPPORTED_EXTENSIONS = new Set([".pptx", ".xlsx", ".xls"]);

export async function scanOfficeFiles(rootPath) {
  const root = path.resolve(rootPath);
  const rootStat = await stat(root);
  if (!rootStat.isDirectory()) throw new Error(`Not a directory: ${root}`);

  const folders = [];
  const files = [];
  const skipped = [];
  await walkDirectory(root, root, folders, files, skipped);

  folders.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
  files.sort((a, b) => a.relativePath.localeCompare(b.relativePath));

  return { root, scannedAt: new Date().toISOString(), folders, files, skipped, summary: summarize(folders, files, skipped) };
}

async function walkDirectory(root, currentDirectory, folders, files, skipped) {
  let entries;
  try {
    entries = await readdir(currentDirectory, { withFileTypes: true });
  } catch (error) {
    skipped.push({ relativePath: path.relative(root, currentDirectory) || ".", reason: error.code || "ACCESS_ERROR" });
    return;
  }

  for (const entry of entries) {
    const absolutePath = path.join(currentDirectory, entry.name);
    if (entry.isDirectory()) {
      folders.push({ name: entry.name, absolutePath, relativePath: path.relative(root, absolutePath) });
      await walkDirectory(root, absolutePath, folders, files, skipped);
      continue;
    }
    if (!entry.isFile()) continue;
    const extension = path.extname(entry.name).toLowerCase();
    if (!SUPPORTED_EXTENSIONS.has(extension)) continue;

    try {
      const fileStat = await stat(absolutePath);
      files.push({ name: entry.name, extension, absolutePath, relativePath: path.relative(root, absolutePath), size: fileStat.size, modifiedAt: fileStat.mtime.toISOString() });
    } catch (error) {
      skipped.push({ relativePath: path.relative(root, absolutePath), reason: error.code || "ACCESS_ERROR" });
    }
  }
}

function summarize(folders, files, skipped) {
  const byExtension = { ".pptx": 0, ".xlsx": 0, ".xls": 0 };
  for (const file of files) byExtension[file.extension] += 1;
  return { folders: folders.length, total: files.length, pptx: byExtension[".pptx"], xlsx: byExtension[".xlsx"], xls: byExtension[".xls"], skipped: skipped.length };
}
