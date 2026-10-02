import { readdir, stat } from "node:fs/promises";
import path from "node:path";

export const SUPPORTED_EXTENSIONS = new Set([".pptx", ".xlsx", ".xls"]);

export async function scanOfficeFiles(rootPath) {
  const root = path.resolve(rootPath);
  const rootStat = await stat(root);

  if (!rootStat.isDirectory()) {
    throw new Error(`Not a directory: ${root}`);
  }

  const folders = [];
  const files = [];
  await walkDirectory(root, root, folders, files);

  folders.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
  files.sort((a, b) => a.relativePath.localeCompare(b.relativePath));

  return {
    root,
    scannedAt: new Date().toISOString(),
    folders,
    files,
    summary: summarize(folders, files),
  };
}

async function walkDirectory(root, currentDirectory, folders, files) {
  const entries = await readdir(currentDirectory, { withFileTypes: true });

  for (const entry of entries) {
    const absolutePath = path.join(currentDirectory, entry.name);

    if (entry.isDirectory()) {
      folders.push({
        name: entry.name,
        absolutePath,
        relativePath: path.relative(root, absolutePath),
      });

      await walkDirectory(root, absolutePath, folders, files);
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

function summarize(folders, files) {
  const byExtension = {
    ".pptx": 0,
    ".xlsx": 0,
    ".xls": 0,
  };

  for (const file of files) {
    byExtension[file.extension] += 1;
  }

  return {
    folders: folders.length,
    total: files.length,
    pptx: byExtension[".pptx"],
    xlsx: byExtension[".xlsx"],
    xls: byExtension[".xls"],
  };
}
