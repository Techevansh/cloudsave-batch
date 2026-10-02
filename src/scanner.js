import { readdir, stat } from "node:fs/promises";
import path from "node:path";

export const SUPPORTED_EXTENSIONS = new Set([".pptx", ".xlsx", ".xls"]);

export async function scanOfficeFiles(rootPath) {
  const root = path.resolve(rootPath);
  const rootStat = await stat(root);

  if (!rootStat.isDirectory()) {
    throw new Error(`Not a directory: ${root}`);
  }

  const files = [];
  await walkDirectory(root, root, files);

  files.sort((a, b) => a.relativePath.localeCompare(b.relativePath));

  return {
    root,
    scannedAt: new Date().toISOString(),
    files,
    summary: summarize(files),
  };
}

async function walkDirectory(root, currentDirectory, files) {
  const entries = await readdir(currentDirectory, { withFileTypes: true });

  for (const entry of entries) {
    const absolutePath = path.join(currentDirectory, entry.name);

    if (entry.isDirectory()) {
      await walkDirectory(root, absolutePath, files);
      continue;
    }

    if (!entry.isFile()) {
      continue;
    }

    const extension = path.extname(entry.name).toLowerCase();

    if (!SUPPORTED_EXTENSIONS.has(extension)) {
      continue;
    }

    const fileStat = await stat(absolutePath);

    files.push({
      name: entry.name,
      extension,
      absolutePath,
      relativePath: path.relative(root, absolutePath),
      size: fileStat.size,
      modifiedAt: fileStat.mtime.toISOString(),
    });
  }
}

function summarize(files) {
  const byExtension = {
    ".pptx": 0,
    ".xlsx": 0,
    ".xls": 0,
  };

  for (const file of files) {
    byExtension[file.extension] += 1;
  }

  return {
    total: files.length,
    pptx: byExtension[".pptx"],
    xlsx: byExtension[".xlsx"],
    xls: byExtension[".xls"],
  };
}
