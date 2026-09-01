const scriptUrl = new URL(document.currentScript?.src || "app.js", location.href);
const initialBase = scriptUrl.pathname.replace(/\/app\.js$/, "").replace(/\/$/, "");
let base = initialBase === "" ? "" : initialBase;
let api = `${base}/api`;
let eventsUrl = "";
let current = "";
let selected = new Set();
let currentItems = [];
let reloadTimer = 0;
let cancelGeneration = 0;

const mainEl = document.getElementById("main");
const rows = document.getElementById("rows");
const statusEl = document.getElementById("status");
const operationsPanel = document.getElementById("operationsPanel");
const operationsEl = document.getElementById("operations");
const globalProgress = document.getElementById("globalProgress");
const toggleOperations = document.getElementById("toggleOperations");
const operationCountEl = document.getElementById("operationCount");
const clearOperationsButton = document.getElementById("clearOperations");
const cancelOperationsButton = document.getElementById("cancelOperations");
const uploadModal = document.getElementById("uploadModal");
const uploadDrop = document.getElementById("uploadDrop");
const filesInput = document.getElementById("files");
const folderInput = document.getElementById("folder");
const operations = new Map();
const uploadOperationIdsByPath = new Map();
const uploadOperationIdsByUploadId = new Map();
const activeRequests = new Map();

function fmtSize(n) {
  if (!n) return "";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let i = 0;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i++;
  }
  return `${n.toFixed(i ? 1 : 0)} ${units[i]}`;
}

function setStatus(text) { statusEl.textContent = text; }
function enc(path) { return encodeURIComponent(path || ""); }
function parseEvent(e) {
  try { return JSON.parse(e.data || "{}"); }
  catch { return {}; }
}

function operationLabel(kind) {
  switch (kind) {
    case "createFolder": return "Create folder";
    case "rename": return "Rename";
    case "move": return "Move";
    case "delete": return "Delete";
    case "completeUpload": return "Finalize upload";
    case "upload": return "Upload";
    default: return kind || "Operation";
  }
}

function isTerminalStatus(status) {
  return status === "done" || status === "failed" || status === "cancelled";
}

function isActiveOperation(operation) {
  return !isTerminalStatus(operation.status);
}

function operationStatusLabel(operation) {
  if (operation.error) return operation.error;
  if (operation.total > 1) {
    return `${operation.status} ${fmtSize(operation.done)} / ${fmtSize(operation.total)}`;
  }
  return operation.status;
}

function trackOperation(input) {
  const id = String(input.id || `operation:${Date.now()}:${Math.random()}`);
  const currentOperation = operations.get(id) || {};
  const next = {
    id,
    kind: input.kind || currentOperation.kind || "operation",
    path: input.path ?? currentOperation.path ?? "",
    status: input.status || currentOperation.status || "queued",
    done: input.done ?? currentOperation.done ?? 0,
    total: input.total ?? currentOperation.total ?? 1,
    error: input.error ?? currentOperation.error ?? ""
  };
  operations.set(id, next);
  renderOperations();
  return next;
}

function trackQueuedOperation(response) {
  if (!response || !response.operationId) return;
  trackOperation({
    id: response.operationId,
    kind: response.operation,
    path: response.path,
    status: "queued",
    done: 0,
    total: 1
  });
}

function updateOperation(id, patch) {
  if (!id) return;
  const currentOperation = operations.get(String(id));
  trackOperation({ id, ...(currentOperation || {}), ...patch });
}

function updateOperationControls() {
  const values = [...operations.values()];
  const activeCount = values.filter(isActiveOperation).length;
  const hasHistory = values.some(o => isTerminalStatus(o.status));

  operationCountEl.textContent = String(activeCount);
  toggleOperations.classList.toggle("has-active", activeCount > 0);
  cancelOperationsButton.disabled = activeCount === 0;
  clearOperationsButton.disabled = !hasHistory;
}

function renderOperations() {
  operationsEl.innerHTML = "";
  const visible = [...operations.values()];
  if (!visible.length) {
    globalProgress.value = 0;
    const empty = document.createElement("div");
    empty.className = "operation-empty";
    empty.textContent = "No operation";
    operationsEl.append(empty);
    updateOperationControls();
    return;
  }

  const active = visible.filter(isActiveOperation);
  const progressSource = active.length ? active : visible;
  const total = progressSource.reduce((a, o) => a + Math.max(o.total || 1, 1), 0) || 1;
  const done = progressSource.reduce((a, o) => a + Math.min(o.done || 0, o.total || 1), 0);
  globalProgress.value = Math.round(done * 100 / total);

  for (const operation of visible) {
    const div = document.createElement("div");
    const statusClass = operation.status === "done" ? "ok" : operation.status === "failed" || operation.status === "cancelled" ? "bad" : "muted";
    div.className = "operation-item";

    const head = document.createElement("div");
    head.className = "operation-head";

    const title = document.createElement("div");
    title.className = "operation-title";
    title.textContent = operation.path || "/";

    const kind = document.createElement("div");
    kind.className = "operation-kind";
    kind.textContent = operationLabel(operation.kind);

    const progress = document.createElement("progress");
    progress.value = Math.min(operation.done || 0, operation.total || 1);
    progress.max = Math.max(operation.total || 1, 1);

    const status = document.createElement("div");
    status.className = statusClass;
    status.textContent = operationStatusLabel(operation);

    head.append(title, kind);
    div.append(head, progress, status);
    operationsEl.append(div);
  }

  updateOperationControls();
}

function cleanupOperationReferences(id) {
  for (const [path, operationId] of uploadOperationIdsByPath) {
    if (operationId === id) {
      uploadOperationIdsByPath.delete(path);
    }
  }
  for (const [uploadId, operationIds] of uploadOperationIdsByUploadId) {
    operationIds.delete(id);
    if (!operationIds.size) {
      uploadOperationIdsByUploadId.delete(uploadId);
    }
  }
  activeRequests.delete(id);
}

function clearOperationHistory() {
  for (const [id, operation] of operations) {
    if (isTerminalStatus(operation.status)) {
      operations.delete(id);
      cleanupOperationReferences(id);
    }
  }
  renderOperations();
  setStatus("Operation history cleared");
}

function setOperationsPanelVisible(visible) {
  operationsPanel.hidden = !visible;
  mainEl.classList.toggle("operations-open", visible);
  toggleOperations.setAttribute("aria-expanded", String(visible));
}

function toggleOperationsPanel() {
  setOperationsPanelVisible(operationsPanel.hidden);
}

async function cancelAllOperations() {
  const active = [...operations.values()].filter(isActiveOperation);
  if (!active.length) return;

  cancelGeneration++;
  for (const operation of active) {
    updateOperation(operation.id, { status: "cancelling", error: "" });
  }
  for (const xhr of activeRequests.values()) {
    xhr.abort();
  }
  activeRequests.clear();

  const hasEvents = eventsUrl && typeof EventSource !== "undefined";
  const response = await apiJson(`${api}/operations/cancel`, {});
  if (!hasEvents) {
    for (const operation of active) {
      updateOperation(operation.id, { status: "cancelled", error: "" });
    }
  }
  setStatus(`Cancellation requested (${response.cancelledOperations || 0} operations, ${response.cancelledUploads || 0} uploads)`);
}

async function apiJson(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  const json = await response.json().catch(() => ({ ok: false, error: "invalid_response" }));
  if (!response.ok || json.ok === false) throw new Error(json.error || response.statusText);
  return json;
}

async function loadConfig() {
  const response = await fetch(`${api}/config`);
  const json = await response.json();
  if (!response.ok || !json.ok) throw new Error(json.error || "config_failed");
  base = (json.prefix || base).replace(/\/$/, "");
  api = json.apiPrefix || `${base}/api`;
  eventsUrl = json.enableEvents ? (json.eventsPrefix || "") : "";
}

function shouldReload(path) {
  path = (path || "").replace(/^\/+|\/+$/g, "");
  return !path || !current || path === current || current.startsWith(`${path}/`) || path.startsWith(`${current}/`);
}

function scheduleReload(path) {
  if (!shouldReload(path)) return;
  clearTimeout(reloadTimer);
  reloadTimer = setTimeout(() => load().catch(err => setStatus(err.message)), 80);
}

function queueReloadFallback(path) {
  if (eventsUrl && typeof EventSource !== "undefined") return;
  for (const delay of [250, 1000, 3000]) {
    setTimeout(() => scheduleReload(path || current), delay);
  }
}

function setupEvents() {
  if (!eventsUrl || typeof EventSource === "undefined") return;
  const es = new EventSource(eventsUrl);
  es.addEventListener("filebrowser.connected", () => setStatus("Live updates connected"));
  es.addEventListener("filebrowser.operation.started", e => {
    const msg = parseEvent(e);
    trackOperation({
      id: msg.operationId,
      kind: msg.operation,
      path: msg.path,
      status: "running",
      done: 0,
      total: 1
    });
    setStatus(`${operationLabel(msg.operation)} started`);
  });
  es.addEventListener("filebrowser.operation.completed", e => {
    const msg = parseEvent(e);
    updateOperation(msg.operationId, {
      kind: msg.operation,
      path: msg.path,
      status: "done",
      done: 1,
      total: 1,
      error: ""
    });
    setStatus(`${operationLabel(msg.operation)} completed`);
  });
  es.addEventListener("filebrowser.operation.failed", e => {
    const msg = parseEvent(e);
    updateOperation(msg.operationId, {
      kind: msg.operation,
      path: msg.path,
      status: "failed",
      done: 1,
      total: 1,
      error: msg.error || "error"
    });
    setStatus(`${operationLabel(msg.operation)} failed: ${msg.error || "error"}`);
  });
  es.addEventListener("filebrowser.operation.cancelled", e => {
    const msg = parseEvent(e);
    updateOperation(msg.operationId, {
      kind: msg.operation,
      path: msg.path,
      status: "cancelled",
      done: 1,
      total: 1,
      error: ""
    });
    setStatus(`${operationLabel(msg.operation)} cancelled`);
  });
  es.addEventListener("filebrowser.changed", e => {
    const msg = parseEvent(e);
    scheduleReload(msg.path || "");
  });
  es.addEventListener("filebrowser.upload.progress", e => {
    const msg = parseEvent(e);
    updateUploadProgress(msg.path, msg.receivedBytes, msg.totalBytes, msg.completed);
  });
  es.addEventListener("filebrowser.upload.completed", e => {
    const msg = parseEvent(e);
    updateUploadProgress(msg.path, msg.size, msg.size, true);
  });
  es.addEventListener("filebrowser.upload.cancelled", e => {
    const msg = parseEvent(e);
    markUploadCancelled(msg);
  });
  es.onerror = () => setStatus("Live updates reconnecting");
}

async function load(path = current) {
  const response = await fetch(`${api}/list?path=${enc(path)}`);
  const json = await response.json();
  if (!response.ok || !json.ok) throw new Error(json.error || "list_failed");
  current = json.path || "";
  currentItems = json.items || [];
  selected.clear();
  renderCrumbs();
  renderRows();
  updateButtons();
  setStatus(current || "/");
}

function renderCrumbs() {
  const el = document.getElementById("crumbs");
  el.innerHTML = "";
  const root = document.createElement("button");
  root.textContent = "/";
  root.onclick = () => load("").catch(err => setStatus(err.message));
  el.append(root);

  let acc = "";
  for (const part of current.split("/").filter(Boolean)) {
    acc = acc ? `${acc}/${part}` : part;
    const target = acc;
    const button = document.createElement("button");
    button.textContent = part;
    button.onclick = () => load(target).catch(err => setStatus(err.message));
    el.append(button);
  }
}

function renderRows() {
  rows.innerHTML = "";
  if (current) {
    const tr = document.createElement("tr");
    tr.innerHTML = `<td class="name"><span>..</span><a href="#" class="file-name file-link">Parent</a></td><td></td><td></td>`;
    tr.querySelector(".file-link").onclick = e => {
      e.preventDefault();
      e.stopPropagation();
      load(parentOf(current)).catch(err => setStatus(err.message));
    };
    tr.ondblclick = () => load(parentOf(current)).catch(err => setStatus(err.message));
    rows.append(tr);
  }

  for (const item of currentItems) {
    const tr = document.createElement("tr");
    tr.dataset.path = item.path;
    tr.innerHTML = `<td class="name"><span>${item.type === "directory" ? "[D]" : "[F]"}</span>${item.type === "directory" ? '<a href="#" class="file-name file-link"></a>' : '<span class="file-name"></span>'}</td><td>${item.type === "file" ? fmtSize(item.size) : ""}</td><td>${new Date(item.modifiedUtc).toLocaleString()}</td>`;
    tr.querySelector(".file-name").textContent = item.name;
    const link = tr.querySelector(".file-link");
    if (link) {
      link.onclick = e => {
        e.preventDefault();
        e.stopPropagation();
        load(item.path).catch(err => setStatus(err.message));
      };
    }
    tr.onclick = e => {
      if (!e.ctrlKey && !e.metaKey) selected.clear();
      selected.has(item.path) ? selected.delete(item.path) : selected.add(item.path);
      renderRows();
      updateButtons();
    };
    tr.ondblclick = () => {
      if (item.type === "directory") load(item.path).catch(err => setStatus(err.message));
    };
    if (selected.has(item.path)) tr.classList.add("selected");
    rows.append(tr);
  }
}

function parentOf(path) {
  const i = path.lastIndexOf("/");
  return i < 0 ? "" : path.slice(0, i);
}

function updateButtons() {
  const one = selected.size === 1;
  document.getElementById("rename").disabled = !one;
  document.getElementById("move").disabled = !selected.size;
  document.getElementById("delete").disabled = !selected.size;
}

function openUploadModal() {
  uploadModal.hidden = false;
  uploadDrop.classList.remove("hot");
}

function closeUploadModal() {
  uploadModal.hidden = true;
  uploadDrop.classList.remove("hot");
}

function beginUpload(files) {
  startUpload(files).catch(err => {
    if ((err.message || "") !== "upload_cancelled") {
      setStatus(err.message || "upload_failed");
    }
  });
}

function runAction(action) {
  action().catch(err => setStatus(err.message || "operation_failed"));
}

document.getElementById("refresh").onclick = () => load().catch(err => setStatus(err.message || "refresh_failed"));
document.getElementById("newFolder").onclick = () => runAction(async () => {
  const name = prompt("Folder name");
  if (!name) return;
  const op = await apiJson(`${api}/folders`, { path: current ? `${current}/${name}` : name });
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(op.path);
});
document.getElementById("rename").onclick = () => runAction(async () => {
  const path = [...selected][0];
  const name = prompt("New name", path.split("/").pop());
  if (!name) return;
  const op = await apiJson(`${api}/rename`, { path, name });
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(op.path);
});
document.getElementById("move").onclick = () => runAction(async () => {
  const destinationDirectory = prompt("Destination folder", current);
  if (destinationDirectory == null) return;
  for (const sourcePath of selected) {
    const op = await apiJson(`${api}/move`, { sourcePath, destinationDirectory });
    trackQueuedOperation(op);
    setStatus(`Queued ${operationLabel(op.operation)}`);
    queueReloadFallback(op.path);
  }
});
document.getElementById("delete").onclick = () => runAction(async () => {
  if (!confirm("Delete selected items?")) return;
  const op = await apiJson(`${api}/delete`, { paths: [...selected] });
  selected.clear();
  updateButtons();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(op.path);
});

toggleOperations.onclick = toggleOperationsPanel;
clearOperationsButton.onclick = clearOperationHistory;
cancelOperationsButton.onclick = () => runAction(cancelAllOperations);
document.getElementById("upload").onclick = openUploadModal;
document.getElementById("chooseFiles").onclick = () => filesInput.click();
document.getElementById("chooseFolder").onclick = () => folderInput.click();
document.getElementById("closeUpload").onclick = closeUploadModal;
document.querySelector("[data-close-upload]").onclick = closeUploadModal;
document.addEventListener("keydown", e => {
  if (e.key === "Escape" && !uploadModal.hidden) closeUploadModal();
});
filesInput.onchange = e => {
  closeUploadModal();
  beginUpload([...e.target.files]);
  e.target.value = "";
};
folderInput.onchange = e => {
  closeUploadModal();
  beginUpload([...e.target.files]);
  e.target.value = "";
};

["dragenter", "dragover"].forEach(ev => uploadDrop.addEventListener(ev, e => {
  e.preventDefault();
  uploadDrop.classList.add("hot");
}));
["dragleave", "drop"].forEach(ev => uploadDrop.addEventListener(ev, e => {
  e.preventDefault();
  uploadDrop.classList.remove("hot");
}));
uploadDrop.addEventListener("drop", async e => {
  try {
    const files = await collectDroppedFiles(e.dataTransfer);
    closeUploadModal();
    beginUpload(files);
  }
  catch (err) {
    setStatus(err.message || "drop_failed");
  }
});

async function collectDroppedFiles(dataTransfer) {
  const items = [...(dataTransfer?.items || [])];
  const entries = items
    .map(item => typeof item.webkitGetAsEntry === "function" ? item.webkitGetAsEntry() : null)
    .filter(Boolean);

  if (!entries.length) {
    return [...(dataTransfer?.files || [])];
  }

  const files = [];
  for (const entry of entries) {
    await walkDroppedEntry(entry, "", files);
  }
  return files;
}

async function walkDroppedEntry(entry, parentPath, files) {
  if (entry.isFile) {
    const file = await new Promise((resolve, reject) => entry.file(resolve, reject));
    files.push({
      file,
      uploadRelativePath: parentPath ? `${parentPath}/${file.name}` : file.name
    });
    return;
  }

  if (!entry.isDirectory) return;
  const nextParent = parentPath ? `${parentPath}/${entry.name}` : entry.name;
  const reader = entry.createReader();
  let batch = [];
  do {
    batch = await new Promise((resolve, reject) => reader.readEntries(resolve, reject));
    for (const child of batch) {
      await walkDroppedEntry(child, nextParent, files);
    }
  } while (batch.length);
}

function markEntriesCancelled(entries) {
  for (const entry of entries) {
    updateOperation(entry.id, {
      status: "cancelled",
      done: entry.done || 0,
      total: entry.size || 1,
      error: ""
    });
  }
}

async function startUpload(files) {
  if (!files.length) return;
  const generation = cancelGeneration;
  const entries = files.map((item, index) => {
    const file = item.file || item;
    const relativePath = item.uploadRelativePath || file.webkitRelativePath || file.name;
    const path = (current ? `${current}/` : "") + relativePath.replaceAll("\\", "/");
    const id = `upload:${Date.now()}:${index}:${path}`;
    uploadOperationIdsByPath.set(path, id);
    trackOperation({ id, kind: "upload", path, status: "queued", done: 0, total: file.size || 1 });
    return {
      id,
      file,
      path,
      size: file.size,
      done: 0,
      status: "queued"
    };
  });

  let session;
  try {
    session = await apiJson(`${api}/uploads`, { files: entries.map(e => ({ path: e.path, size: e.size })) });
  }
  catch (err) {
    for (const entry of entries) {
      updateOperation(entry.id, { status: "failed", error: err.message || "upload_failed" });
    }
    throw err;
  }

  const uploadId = String(session.uploadId);
  uploadOperationIdsByUploadId.set(uploadId, new Set(entries.map(e => e.id)));
  if (generation !== cancelGeneration) {
    markEntriesCancelled(entries);
    await apiJson(`${api}/operations/cancel`, {}).catch(() => null);
    throw new Error("upload_cancelled");
  }

  for (const entry of entries) {
    try {
      if (generation !== cancelGeneration) {
        throw new Error("upload_cancelled");
      }
      updateOperation(entry.id, { status: "uploading" });
      if (entry.size <= session.chunkThresholdBytes) {
        await sendBlob(`${api}/uploads/${session.uploadId}/files`, entry, entry.file, 0, generation);
      }
      else {
        for (let offset = 0; offset < entry.size; offset += session.chunkBytes) {
          if (generation !== cancelGeneration) {
            throw new Error("upload_cancelled");
          }
          await sendBlob(`${api}/uploads/${session.uploadId}/chunks`, entry, entry.file.slice(offset, offset + session.chunkBytes), offset, generation);
        }
      }
      entry.status = "uploaded";
      updateOperation(entry.id, { status: "uploaded", done: entry.size || 1, total: entry.size || 1 });
    }
    catch (err) {
      const cancelled = (err.message || "") === "upload_cancelled";
      entry.status = cancelled ? "cancelled" : (err.message || "failed");
      updateOperation(entry.id, { status: cancelled ? "cancelled" : "failed", error: cancelled ? "" : entry.status });
      if (cancelled) {
        markEntriesCancelled(entries.filter(e => !isTerminalStatus(operations.get(e.id)?.status)));
      }
      throw err;
    }
  }
  if (generation !== cancelGeneration) {
    markEntriesCancelled(entries);
    await apiJson(`${api}/operations/cancel`, {}).catch(() => null);
    throw new Error("upload_cancelled");
  }
  const op = await apiJson(`${api}/uploads/${session.uploadId}/complete`, {});
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(current);
}

function sendBlob(url, entry, blob, offset, generation) {
  if (generation !== cancelGeneration) {
    return Promise.reject(new Error("upload_cancelled"));
  }

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", url);
    xhr.setRequestHeader("X-File-Path", encodeURIComponent(entry.path));
    xhr.setRequestHeader("X-Chunk-Offset", String(offset));
    xhr.upload.onprogress = e => {
      if (e.lengthComputable) {
        entry.done = Math.max(entry.done, offset + e.loaded);
        updateOperation(entry.id, {
          status: "uploading",
          done: entry.done,
          total: entry.size || 1
        });
      }
    };
    xhr.onload = () => {
      activeRequests.delete(entry.id);
      xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(xhr.responseText || xhr.statusText));
    };
    xhr.onerror = () => {
      activeRequests.delete(entry.id);
      reject(new Error("network_error"));
    };
    xhr.onabort = () => {
      activeRequests.delete(entry.id);
      reject(new Error("upload_cancelled"));
    };
    activeRequests.set(entry.id, xhr);
    if (generation !== cancelGeneration) {
      xhr.abort();
      return;
    }
    xhr.send(blob);
  });
}

function updateUploadProgress(path, receivedBytes, totalBytes, completed) {
  if (!path) return;
  let id = uploadOperationIdsByPath.get(path);
  if (!id) {
    id = `upload:${path}`;
    uploadOperationIdsByPath.set(path, id);
  }

  const size = totalBytes || operations.get(id)?.total || 1;
  trackOperation({
    id,
    kind: "upload",
    path,
    status: completed ? "done" : "server received",
    done: completed ? size : (receivedBytes || 0),
    total: size,
    error: ""
  });
}

function markUploadCancelled(message) {
  const ids = new Set();
  const paths = new Map();
  if (message.uploadId && uploadOperationIdsByUploadId.has(String(message.uploadId))) {
    for (const id of uploadOperationIdsByUploadId.get(String(message.uploadId))) {
      ids.add(id);
    }
  }
  for (const path of message.files || []) {
    let id = uploadOperationIdsByPath.get(path);
    if (!id) {
      id = `upload:${path}`;
      uploadOperationIdsByPath.set(path, id);
    }
    ids.add(id);
    paths.set(id, path);
  }

  for (const id of ids) {
    const operation = operations.get(id);
    updateOperation(id, {
      kind: "upload",
      path: operation?.path || paths.get(id) || "",
      status: "cancelled",
      done: operation?.done || 0,
      total: operation?.total || 1,
      error: ""
    });
  }
  setStatus("Upload cancelled");
}

renderOperations();
setOperationsPanelVisible(false);
loadConfig()
  .then(() => {
    setupEvents();
    return load();
  })
  .catch(err => setStatus(err.message));
