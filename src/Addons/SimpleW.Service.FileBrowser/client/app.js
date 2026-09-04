const scriptUrl = new URL(document.currentScript?.src || "app.js", location.href);
const initialBase = scriptUrl.pathname.replace(/\/app\.js$/, "").replace(/\/$/, "");
let base = initialBase === "" ? "" : initialBase;
let api = `${base}/api`;
let eventsUrl = "";
let current = "";
let selected = new Set();
let currentItems = [];
let search = "";
let sort = "name";
let direction = "asc";
let pageSize = 100;
let defaultPageSize = 100;
let maxPageSize = 1000;
let continuationToken = "";
let nextContinuationToken = "";
let cursorHistory = [];
let loadGeneration = 0;
let searchTimer = 0;
let reloadTimer = 0;
let trashReloadTimer = 0;
let cancelGeneration = 0;
let operationRenderFrame = 0;

const mainEl = document.getElementById("main");
const browserPane = document.querySelector(".pane");
const browserBar = document.querySelector(".browser-bar");
const rows = document.getElementById("rows");
const statusEl = document.getElementById("status");
const openTrashButton = document.getElementById("openTrash");
const trashCount = document.getElementById("trashCount");
const themeToggle = document.getElementById("themeToggle");
const themeGlyph = document.getElementById("themeGlyph");
const searchInput = document.getElementById("search");
const clearSearchButton = document.getElementById("clearSearch");
const pageSizeSelect = document.getElementById("pageSize");
const pageSummary = document.getElementById("pageSummary");
const previousPageButton = document.getElementById("previousPage");
const nextPageButton = document.getElementById("nextPage");
const selectionBar = document.getElementById("selectionBar");
const selectionSummary = document.getElementById("selectionSummary");
const selectionToggle = document.getElementById("selectionToggle");
const downloadSelectedButton = document.getElementById("downloadSelected");
const archiveSelectedButton = document.getElementById("archiveSelected");
const operationsPanel = document.getElementById("operationsPanel");
const operationsEl = document.getElementById("operations");
const globalProgress = document.getElementById("globalProgress");
const toggleOperations = document.getElementById("toggleOperations");
const operationCountEl = document.getElementById("operationCount");
const clearOperationsButton = document.getElementById("clearOperations");
const cancelOperationsButton = document.getElementById("cancelOperations");
const newFolderModal = document.getElementById("newFolderModal");
const newFolderLocation = document.getElementById("newFolderLocation");
const newFolderName = document.getElementById("newFolderName");
const newFolderDestinationPreview = document.getElementById("newFolderDestinationPreview");
const confirmNewFolderButton = document.getElementById("confirmNewFolder");
const uploadModal = document.getElementById("uploadModal");
const uploadDrop = document.getElementById("uploadDrop");
const uploadDestination = document.getElementById("uploadDestination");
const uploadStaging = document.getElementById("uploadStaging");
const startUploadButton = document.getElementById("startUpload");
const filesInput = document.getElementById("files");
const folderInput = document.getElementById("folder");
const trashModal = document.getElementById("trashModal");
const trashList = document.getElementById("trashList");
const emptyTrashButton = document.getElementById("emptyTrash");
const purgeModal = document.getElementById("purgeModal");
const purgeSelectionSummary = document.getElementById("purgeSelectionSummary");
const confirmPurgeButton = document.getElementById("confirmPurge");
const restoreElsewhereModal = document.getElementById("restoreElsewhereModal");
const restoreElsewhereItem = document.getElementById("restoreElsewhereItem");
const restoreElsewhereDestination = document.getElementById("restoreElsewhereDestination");
const restoreElsewherePreview = document.getElementById("restoreElsewherePreview");
const confirmRestoreElsewhereButton = document.getElementById("confirmRestoreElsewhere");
const renameModal = document.getElementById("renameModal");
const renameSource = document.getElementById("renameSource");
const renameName = document.getElementById("renameName");
const renameDestinationPreview = document.getElementById("renameDestinationPreview");
const confirmRenameButton = document.getElementById("confirmRename");
const moveModal = document.getElementById("moveModal");
const moveSelectionSummary = document.getElementById("moveSelectionSummary");
const moveDestination = document.getElementById("moveDestination");
const moveDestinationPreview = document.getElementById("moveDestinationPreview");
const confirmMoveButton = document.getElementById("confirmMove");
const deleteModal = document.getElementById("deleteModal");
const deleteSelectionSummary = document.getElementById("deleteSelectionSummary");
const deleteMessage = document.getElementById("deleteMessage");
const confirmDeleteButton = document.getElementById("confirmDelete");
const archiveModal = document.getElementById("archiveModal");
const archiveSelectionSummary = document.getElementById("archiveSelectionSummary");
const archiveName = document.getElementById("archiveName");
const archiveDestinationPreview = document.getElementById("archiveDestinationPreview");
const confirmArchiveButton = document.getElementById("confirmArchive");
const extractModal = document.getElementById("extractModal");
const extractArchiveName = document.getElementById("extractArchiveName");
const extractFolderName = document.getElementById("extractFolderName");
const extractFolderPreview = document.getElementById("extractFolderPreview");
const extractHerePreview = document.getElementById("extractHerePreview");
const extractToFolder = document.getElementById("extractToFolder");
const confirmExtractButton = document.getElementById("confirmExtract");
const operations = new Map();
const uploadOperationIdsByPath = new Map();
const uploadOperationIdsByUploadId = new Map();
const activeRequests = new Map();
let stagedUploadItems = [];
let stagedUploadSequence = 0;
let uploadDestinationPath = "";
let browserFileDragDepth = 0;
let trashItems = [];
let purgeTrashIds = [];
let purgeAllTrash = false;
let restoreElsewhereTrashId = "";
let renameTargetPath = "";
let moveSourcePaths = [];
let deleteSourcePaths = [];
let archiveSourcePaths = [];
let archiveDestinationDirectory = "";
let extractArchivePath = "";
let newFolderParentPath = "";

function isModalOpen() {
  return !newFolderModal.hidden
    || !uploadModal.hidden
    || !trashModal.hidden
    || !purgeModal.hidden
    || !restoreElsewhereModal.hidden
    || !renameModal.hidden
    || !moveModal.hidden
    || !deleteModal.hidden
    || !archiveModal.hidden
    || !extractModal.hidden;
}

function fmtSize(n) {
  if (n == null || n === "") return "\u2014";
  n = Number(n);
  if (!Number.isFinite(n)) return "\u2014";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let i = 0;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i++;
  }
  return `${n.toFixed(i ? 1 : 0)} ${units[i]}`;
}

function setStatus(text) { statusEl.textContent = text; }
function readStoredTheme() {
  try {
    const value = localStorage.getItem("simplew.filebrowser.theme");
    return ["system", "light", "dark"].includes(value) ? value : "system";
  }
  catch {
    return "system";
  }
}

function applyTheme(mode, persist = true) {
  const modes = ["system", "light", "dark"];
  const labels = { system: "System", light: "Light", dark: "Dark" };
  const glyphs = { system: "\u25d0", light: "\u2600", dark: "\u263e" };
  const theme = modes.includes(mode) ? mode : "system";
  const next = modes[(modes.indexOf(theme) + 1) % modes.length];

  document.documentElement.dataset.theme = theme;
  themeGlyph.textContent = glyphs[theme];
  themeToggle.setAttribute("aria-label", `Color mode: ${labels[theme]}. Switch to ${labels[next]}`);
  themeToggle.title = `Color mode: ${labels[theme]}. Switch to ${labels[next]}`;
  if (persist) {
    try { localStorage.setItem("simplew.filebrowser.theme", theme); }
    catch { }
  }
}

applyTheme(readStoredTheme(), false);

function syncStickyTableHeader() {
  const height = Math.ceil(browserBar.getBoundingClientRect().height);
  document.documentElement.style.setProperty("--browser-bar-height", `${height}px`);
}

const browserBarResizeObserver = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(syncStickyTableHeader);
browserBarResizeObserver?.observe(browserBar);
window.addEventListener("resize", syncStickyTableHeader);
syncStickyTableHeader();

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
    case "restore": return "Restore";
    case "purge": return "Delete permanently";
    case "emptyTrash": return "Empty trash";
    case "archive": return "Create archive";
    case "extract": return "Extract";
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
    return `${fmtSize(operation.done)} / ${fmtSize(operation.total)}`;
  }
  return operationStateLabel(operation.status);
}

function operationStateLabel(status) {
  switch (status) {
    case "queued": return "Queued";
    case "running": return "In progress";
    case "cancelling": return "Cancelling";
    case "done": return "Completed";
    case "failed": return "Failed";
    case "cancelled": return "Cancelled";
    default: return status || "Pending";
  }
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
  scheduleOperationRender();
  return next;
}

function scheduleOperationRender() {
  if (operationRenderFrame) return;
  operationRenderFrame = requestAnimationFrame(() => {
    operationRenderFrame = 0;
    renderOperations();
  });
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
  const queuedCount = values.filter(operation => operation.status === "queued").length;
  const runningCount = values.filter(operation => isActiveOperation(operation) && operation.status !== "queued").length;
  const activeCount = runningCount + queuedCount;
  const hasHistory = values.some(o => isTerminalStatus(o.status));

  operationCountEl.textContent = `${runningCount}/${queuedCount}`;
  operationCountEl.hidden = activeCount === 0;
  operationCountEl.title = `${runningCount} in progress, ${queuedCount} remaining`;
  const operationsLabel = `Operations: ${runningCount} in progress, ${queuedCount} remaining`;
  toggleOperations.setAttribute("aria-label", operationsLabel);
  toggleOperations.title = operationsLabel;
  toggleOperations.classList.toggle("has-active", activeCount > 0);
  cancelOperationsButton.disabled = activeCount === 0;
  clearOperationsButton.disabled = !hasHistory;
}

function renderOperations() {
  operationsEl.innerHTML = "";
  const visible = [...operations.values()];
  if (!visible.length) {
    globalProgress.value = 0;
    globalProgress.hidden = true;
    const empty = document.createElement("div");
    empty.className = "operation-empty";
    empty.textContent = "No operation";
    operationsEl.append(empty);
    updateOperationControls();
    return;
  }

  globalProgress.hidden = false;
  const active = visible.filter(isActiveOperation);
  const progressSource = active.length ? active : visible;
  const total = progressSource.reduce((a, o) => a + Math.max(o.total || 1, 1), 0) || 1;
  const done = progressSource.reduce((a, o) => a + Math.min(o.done || 0, o.total || 1), 0);
  globalProgress.value = Math.round(done * 100 / total);

  for (const operation of visible) {
    const div = document.createElement("div");
    const state = operation.status === "done" ? "done"
      : operation.status === "failed" ? "failed"
      : operation.status === "cancelled" ? "cancelled"
      : operation.status === "running" ? "running"
      : operation.status === "cancelling" ? "cancelling"
      : "queued";
    div.className = `operation-item operation-state-${state}`;

    const head = document.createElement("div");
    head.className = "operation-head";

    const kind = document.createElement("div");
    kind.className = "operation-kind";
    kind.textContent = operationLabel(operation.kind);

    const status = document.createElement("span");
    status.className = "operation-status-badge";
    status.textContent = operationStateLabel(operation.status);

    const title = document.createElement("div");
    title.className = "operation-title";
    title.textContent = operation.path || "/";
    title.title = operation.path || "/";

    const progressRow = document.createElement("div");
    progressRow.className = "operation-progress-row";

    const progress = document.createElement("progress");
    progress.className = "operation-progress";
    progress.max = Math.max(operation.total || 1, 1);
    if (operation.status === "running" && operation.done <= 0 && operation.total <= 1) {
      progress.removeAttribute("value");
    } else {
      progress.value = Math.min(operation.done || 0, operation.total || 1);
    }

    const progressLabel = document.createElement("span");
    progressLabel.className = "operation-progress-label";
    progressLabel.textContent = operationStatusLabel(operation);
    progressLabel.title = progressLabel.textContent;

    head.append(kind, status);
    progressRow.append(progress, progressLabel);
    div.append(head, title, progressRow);
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
  mainEl.classList.toggle("operations-open", visible);
  toggleOperations.setAttribute("aria-expanded", String(visible));
  operationsPanel.setAttribute("aria-hidden", String(!visible));
}

function toggleOperationsPanel() {
  setOperationsPanelVisible(!mainEl.classList.contains("operations-open"));
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

function legacyTrashDisplayName(id) {
  const match = id.match(/^\d{17}(?:-\d+)?-(.+)$/);
  return match ? match[1] : id;
}

function updateTrashCount() {
  trashCount.textContent = `(${trashItems.length})`;
  trashCount.hidden = trashItems.length === 0;
  openTrashButton.title = trashItems.length
    ? `${trashItems.length} item${trashItems.length === 1 ? "" : "s"} in trash`
    : "Trash is empty";
  emptyTrashButton.disabled = trashItems.length === 0;
}

function renderTrashItems() {
  trashList.innerHTML = "";
  if (!trashItems.length) {
    const empty = document.createElement("div");
    empty.className = "trash-list-message";
    empty.textContent = "Trash is empty";
    trashList.append(empty);
    updateTrashCount();
    return;
  }

  for (const item of trashItems) {
    const row = document.createElement("div");
    row.className = "trash-item";

    const main = document.createElement("div");
    main.className = "trash-item-main";
    const icon = document.createElement("span");
    icon.className = item.type === "directory" ? "folder-icon" : "file-icon";
    icon.setAttribute("aria-hidden", "true");

    const details = document.createElement("div");
    details.className = "trash-item-details";
    const name = document.createElement("strong");
    name.className = "trash-item-name";
    name.textContent = item.canRestore ? item.name : legacyTrashDisplayName(item.name || item.id);
    name.title = name.textContent;
    const path = document.createElement("span");
    path.className = "trash-item-path";
    path.textContent = item.canRestore && item.originalPath
      ? `Original location: ${displayBrowserPath(item.originalPath)}`
      : "Original location unavailable (legacy item)";
    path.title = path.textContent;
    const date = document.createElement("span");
    date.className = "trash-item-date";
    const deletedDate = new Date(item.deletedUtc);
    const dateLabel = Number.isNaN(deletedDate.getTime()) ? "Unknown deletion date" : `Deleted ${deletedDate.toLocaleString()}`;
    date.textContent = item.type === "file" ? `${dateLabel} · ${fmtSize(item.size)}` : dateLabel;
    details.append(name, path, date);
    main.append(icon, details);

    const actions = document.createElement("div");
    actions.className = "trash-item-actions";
    const restore = document.createElement("button");
    restore.type = "button";
    restore.textContent = item.canRestore ? "Restore" : "Restore elsewhere";
    restore.disabled = !item.canRestore && !item.canRestoreElsewhere;
    restore.title = item.canRestore
      ? "Restore to the original location"
      : "Choose a new destination for this legacy item";
    restore.onclick = item.canRestore
      ? () => runAction(() => restoreTrashItems([item.id]))
      : () => openRestoreElsewhereModal(item);
    const remove = document.createElement("button");
    remove.type = "button";
    remove.textContent = "Delete permanently";
    remove.onclick = () => openPurgeModal([item.id], false);
    actions.append(restore, remove);

    row.append(main, actions);
    trashList.append(row);
  }
  updateTrashCount();
}

async function loadTrash() {
  const response = await fetch(`${api}/trash`);
  const json = await response.json().catch(() => ({ ok: false, error: "invalid_response" }));
  if (!response.ok || !json.ok) throw new Error(json.error || "trash_load_failed");
  trashItems = Array.isArray(json.items) ? json.items : [];
  renderTrashItems();
}

async function openTrashModal() {
  trashModal.hidden = false;
  trashList.innerHTML = '<div class="trash-list-message">Loading trash...</div>';
  try {
    await loadTrash();
  }
  catch (err) {
    trashList.innerHTML = '<div class="trash-list-message bad">Unable to load trash</div>';
    throw err;
  }
}

function closeTrashModal() {
  trashModal.hidden = true;
}

async function restoreTrashItems(ids, destinationPath = "") {
  const body = { ids };
  if (destinationPath) body.destinationPath = destinationPath;
  const op = await apiJson(`${api}/trash/restore`, body);
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueTrashReloadFallback();
  queueReloadFallback(op.path);
}

function updateRestoreElsewhereDestination() {
  const rawDestination = restoreElsewhereDestination.value.trim();
  const destinationPath = normalizedMoveDestination(rawDestination);
  const segments = destinationPath.split("/").filter(Boolean);
  const validDestination = destinationPath !== ""
    && !rawDestination.includes("\\")
    && segments.every(segment => segment !== "." && segment !== "..");
  const preview = restoreElsewherePreview.parentElement;
  restoreElsewherePreview.textContent = validDestination
    ? displayBrowserPath(destinationPath)
    : "Enter a complete destination path";
  preview.classList.toggle("bad", !validDestination);
  confirmRestoreElsewhereButton.disabled = !validDestination;
}

function openRestoreElsewhereModal(item) {
  restoreElsewhereTrashId = item.id;
  const displayName = legacyTrashDisplayName(item.name || item.id);
  restoreElsewhereItem.textContent = displayName;
  restoreElsewhereItem.title = displayName;
  restoreElsewhereDestination.value = combineBrowserPath(current, displayName);
  updateRestoreElsewhereDestination();
  restoreElsewhereModal.hidden = false;
  restoreElsewhereDestination.focus();
  const nameStart = Math.max(0, restoreElsewhereDestination.value.lastIndexOf("/") + 1);
  restoreElsewhereDestination.setSelectionRange(nameStart, restoreElsewhereDestination.value.length);
}

function closeRestoreElsewhereModal() {
  restoreElsewhereModal.hidden = true;
  restoreElsewhereTrashId = "";
}

async function performRestoreElsewhere() {
  updateRestoreElsewhereDestination();
  if (confirmRestoreElsewhereButton.disabled || !restoreElsewhereTrashId) return;
  const destinationPath = normalizedMoveDestination(restoreElsewhereDestination.value);
  await restoreTrashItems([restoreElsewhereTrashId], destinationPath);
  closeRestoreElsewhereModal();
}

function openPurgeModal(ids, all) {
  if (!ids.length) return;
  purgeTrashIds = [...ids];
  purgeAllTrash = all;
  const selectedItem = trashItems.find(item => item.id === ids[0]);
  document.getElementById("purgeTitle").textContent = all ? "Empty trash" : "Delete permanently";
  purgeSelectionSummary.textContent = all
    ? `All ${ids.length} item${ids.length === 1 ? "" : "s"}`
    : selectedItem?.name || legacyTrashDisplayName(ids[0]);
  document.getElementById("purgeMessage").textContent = all
    ? "Every item in the trash will be deleted permanently. This action cannot be undone."
    : "This item will be deleted permanently. This action cannot be undone.";
  confirmPurgeButton.textContent = all ? "Empty trash" : "Delete permanently";
  purgeModal.hidden = false;
  confirmPurgeButton.focus();
}

function closePurgeModal() {
  purgeModal.hidden = true;
  purgeTrashIds = [];
  purgeAllTrash = false;
}

async function performPurge() {
  if (!purgeTrashIds.length) return;
  const endpoint = purgeAllTrash ? `${api}/trash/empty` : `${api}/trash/delete`;
  const op = await apiJson(endpoint, purgeAllTrash ? {} : { ids: purgeTrashIds });
  closePurgeModal();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueTrashReloadFallback();
}

async function loadConfig() {
  const response = await fetch(`${api}/config`);
  const json = await response.json();
  if (!response.ok || !json.ok) throw new Error(json.error || "config_failed");
  base = (json.prefix || base).replace(/\/$/, "");
  api = json.apiPrefix || `${base}/api`;
  eventsUrl = json.enableEvents ? (json.eventsPrefix || "") : "";
  defaultPageSize = Number(json.defaultPageSize) || 100;
  maxPageSize = Number(json.maxPageSize) || 1000;
  pageSize = defaultPageSize;
  populatePageSizes();
}

function populatePageSizes() {
  const sizes = [25, 50, 100, 250, 500, 1000]
    .filter(value => value <= maxPageSize);
  if (!sizes.includes(defaultPageSize)) sizes.push(defaultPageSize);
  sizes.sort((a, b) => a - b);

  pageSizeSelect.innerHTML = "";
  for (const value of sizes) {
    const option = document.createElement("option");
    option.value = String(value);
    option.textContent = String(value);
    pageSizeSelect.append(option);
  }
  pageSizeSelect.value = String(defaultPageSize);
}

function readNavigationState(state = history.state) {
  const params = new URL(location.href).searchParams;
  const requestedSort = params.get("sort") || "name";
  const requestedDirection = params.get("direction") || "asc";
  const requestedPageSize = Number(params.get("pageSize"));

  current = (params.get("path") || "").replace(/^\/+|\/+$/g, "");
  search = (params.get("search") || "").trim();
  sort = ["name", "size", "modified"].includes(requestedSort) ? requestedSort : "name";
  direction = ["asc", "desc"].includes(requestedDirection) ? requestedDirection : "asc";
  pageSize = Number.isInteger(requestedPageSize) && requestedPageSize > 0 && requestedPageSize <= maxPageSize
    ? requestedPageSize
    : defaultPageSize;

  const saved = state?.fileBrowser;
  continuationToken = typeof saved?.continuationToken === "string" ? saved.continuationToken : "";
  cursorHistory = Array.isArray(saved?.cursorHistory) && saved.cursorHistory.every(value => typeof value === "string")
    ? [...saved.cursorHistory]
    : [];
  searchInput.value = search;
  clearSearchButton.hidden = !search;
  pageSizeSelect.value = String(pageSize);
  if (!pageSizeSelect.value) {
    const option = document.createElement("option");
    option.value = String(pageSize);
    option.textContent = String(pageSize);
    pageSizeSelect.append(option);
    pageSizeSelect.value = String(pageSize);
  }
}

function syncNavigationState(mode) {
  if (mode === "none") return;

  const url = new URL(location.href);
  url.searchParams.set("path", current);
  url.searchParams.set("search", search);
  url.searchParams.set("sort", sort);
  url.searchParams.set("direction", direction);
  url.searchParams.set("pageSize", String(pageSize));
  const state = {
    ...(history.state || {}),
    fileBrowser: {
      continuationToken,
      cursorHistory: [...cursorHistory]
    }
  };
  history[mode === "push" ? "pushState" : "replaceState"](state, "", url);
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

function operationChangesTrash(kind) {
  return kind === "delete" || kind === "restore" || kind === "purge" || kind === "emptyTrash";
}

function scheduleTrashReload(delay = 80) {
  clearTimeout(trashReloadTimer);
  trashReloadTimer = setTimeout(() => loadTrash().catch(err => setStatus(err.message || "trash_load_failed")), delay);
}

function queueTrashReloadFallback() {
  if (eventsUrl && typeof EventSource !== "undefined") return;
  for (const delay of [250, 1000, 3000]) {
    setTimeout(() => scheduleTrashReload(), delay);
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
    if (operationChangesTrash(msg.operation)) scheduleTrashReload();
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
    if (operationChangesTrash(msg.operation)) scheduleTrashReload();
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

async function load(path = current, options = {}) {
  const samePath = path === current;
  const requestedSearch = search;
  const requestedToken = options.token ?? (samePath ? continuationToken : "");
  const requestedHistory = options.cursorHistory ?? (samePath ? cursorHistory : []);
  const historyMode = options.historyMode || "replace";
  const generation = ++loadGeneration;
  const query = new URLSearchParams({
    path,
    search,
    sort,
    direction,
    pageSize: String(pageSize)
  });
  if (requestedToken) query.set("continuationToken", requestedToken);

  selected.clear();
  updateButtons();
  renderListMessage("Loading...");
  pageSummary.textContent = "Loading...";
  previousPageButton.disabled = true;
  nextPageButton.disabled = true;

  try {
    const response = await fetch(`${api}/list?${query}`);
    const json = await response.json().catch(() => ({ ok: false, error: "invalid_response" }));
    if (generation !== loadGeneration || searchInput.value.trim() !== requestedSearch) return;
    if (!response.ok || !json.ok) {
      if (json.error === "invalid_continuation_token" && requestedToken && options.retryInvalidToken !== false) {
        return load(path, {
          token: "",
          cursorHistory: [],
          historyMode: "replace",
          retryInvalidToken: false
        });
      }
      throw new Error(json.error || "list_failed");
    }

    const items = json.items || [];
    if (!items.length && requestedToken && requestedHistory.length) {
      const previousHistory = [...requestedHistory];
      const previousToken = previousHistory.pop() || "";
      return load(path, {
        token: previousToken,
        cursorHistory: previousHistory,
        historyMode: "replace"
      });
    }

    current = json.path || "";
    search = json.search || "";
    sort = json.sort || "name";
    direction = json.direction || "asc";
    pageSize = Number(json.pageSize) || defaultPageSize;
    currentItems = items;
    continuationToken = requestedToken;
    nextContinuationToken = json.nextContinuationToken || "";
    cursorHistory = [...requestedHistory];
    selected.clear();
    searchInput.value = search;
    clearSearchButton.hidden = !search;
    pageSizeSelect.value = String(pageSize);
    renderCrumbs();
    renderRows();
    updateButtons();
    updateListControls();
    syncNavigationState(historyMode);
    setStatus(`${current || "/"} - ${currentItems.length} item${currentItems.length === 1 ? "" : "s"}`);
  }
  catch (err) {
    if (generation !== loadGeneration || searchInput.value.trim() !== requestedSearch) return;
    renderListMessage(`Unable to load files: ${err.message || "list_failed"}`, true);
    updateListControls();
    throw err;
  }
}

function navigateTo(path) {
  clearTimeout(searchTimer);
  searchInput.value = search;
  clearSearchButton.hidden = !search;
  return load(path, { token: "", cursorHistory: [], historyMode: "push" });
}

function renderListMessage(message, isError = false) {
  rows.innerHTML = "";
  const tr = document.createElement("tr");
  tr.className = `list-message${isError ? " bad" : ""}`;
  const td = document.createElement("td");
  td.colSpan = 3;
  td.textContent = message;
  tr.append(td);
  rows.append(tr);
}

function updateSortIndicators(previewSort = "") {
  for (const header of document.querySelectorAll("th[aria-sort]")) {
    const button = header.querySelector("[data-sort]");
    const active = button.dataset.sort === sort;
    const previewed = button.dataset.sort === previewSort;
    const indicator = button.querySelector(".sort-indicator");
    header.setAttribute("aria-sort", active ? (direction === "asc" ? "ascending" : "descending") : "none");
    indicator.textContent = previewed
      ? (active && direction === "asc" ? "\u2193" : "\u2191")
      : (active ? (direction === "asc" ? "\u2191" : "\u2193") : "");
  }
}

function updateListControls() {
  pageSummary.textContent = `Page ${cursorHistory.length + 1} - ${currentItems.length} item${currentItems.length === 1 ? "" : "s"}`;
  previousPageButton.disabled = cursorHistory.length === 0;
  nextPageButton.disabled = !nextContinuationToken;
  clearSearchButton.hidden = !searchInput.value;
  const previewButton = document.querySelector("[data-sort]:hover, [data-sort]:focus");
  updateSortIndicators(previewButton?.dataset.sort || "");
}

function renderCrumbs() {
  const el = document.getElementById("crumbs");
  el.innerHTML = "";
  const root = document.createElement("button");
  root.textContent = "/";
  root.onclick = () => navigateTo("").catch(err => setStatus(err.message));
  el.append(root);

  let acc = "";
  for (const part of current.split("/").filter(Boolean)) {
    acc = acc ? `${acc}/${part}` : part;
    const target = acc;
    const button = document.createElement("button");
    button.textContent = part;
    button.onclick = () => navigateTo(target).catch(err => setStatus(err.message));
    el.append(button);
  }
}

function renderRows() {
  rows.innerHTML = "";
  if (current) {
    const tr = document.createElement("tr");
    tr.className = "directory-row";
    tr.innerHTML = `<td class="name"><span>..</span><a href="#" class="file-name file-link">Parent</a></td><td>\u2014</td><td></td>`;
    tr.querySelector(".file-link").onclick = e => {
      e.preventDefault();
      e.stopPropagation();
      navigateTo(parentOf(current)).catch(err => setStatus(err.message));
    };
    tr.onclick = () => navigateTo(parentOf(current)).catch(err => setStatus(err.message));
    rows.append(tr);
  }

  if (!currentItems.length) {
    const tr = document.createElement("tr");
    tr.className = "list-message";
    const td = document.createElement("td");
    td.colSpan = 3;
    td.textContent = search ? "No matching files" : "No files";
    tr.append(td);
    rows.append(tr);
    return;
  }

  for (const item of currentItems) {
    const tr = document.createElement("tr");
    tr.dataset.path = item.path;
    const isSelected = selected.has(item.path);
    const itemIcon = item.type === "directory"
      ? '<span class="folder-icon" aria-hidden="true"></span>'
      : '<span class="file-icon" aria-hidden="true"></span>';
    const extractAction = item.type === "file" && item.name.toLowerCase().endsWith(".zip")
      ? '<button type="button" data-item-action="extract">Extract</button>'
      : "";
    const itemActions = `<div class="item-row-actions" role="group"><button type="button" data-item-action="archive">Archive</button>${extractAction}<button type="button" data-item-action="rename">Rename</button><button type="button" data-item-action="move">Move</button><button type="button" data-item-action="delete">Delete</button></div>`;
    tr.innerHTML = `<td class="name"><input class="row-selection" type="checkbox">${itemIcon}<a href="#" class="file-name file-link"></a>${itemActions}</td><td>${item.type === "file" ? fmtSize(item.size) : "\u2014"}</td><td>${new Date(item.modifiedUtc).toLocaleString()}</td>`;
    tr.querySelector(".file-name").textContent = item.name;
    const checkbox = tr.querySelector(".row-selection");
    checkbox.checked = isSelected;
    checkbox.setAttribute("aria-label", `Select ${item.name}`);
    checkbox.onclick = e => e.stopPropagation();
    checkbox.onchange = () => setRowSelection(tr, checkbox, item.path, checkbox.checked);
    const link = tr.querySelector(".file-link");
    tr.querySelector(".item-row-actions").setAttribute("aria-label", `Actions for ${item.name}`);
    const actionHandlers = {
      archive: async () => openArchiveModal([item.path]),
      extract: async () => openExtractModal(item.path),
      rename: () => renameItem(item.path),
      move: () => moveItems([item.path]),
      delete: () => deleteItems([item.path])
    };
    for (const button of tr.querySelectorAll("[data-item-action]")) {
      button.onclick = e => {
        e.preventDefault();
        e.stopPropagation();
        runAction(actionHandlers[button.dataset.itemAction]);
      };
    }
    if (item.type === "directory") {
      tr.classList.add("directory-row");
      link.onclick = e => {
        e.preventDefault();
        e.stopPropagation();
        navigateTo(item.path).catch(err => setStatus(err.message));
      };
      tr.onclick = () => navigateTo(item.path).catch(err => setStatus(err.message));
    }
    else {
      link.href = `${api}/download?${new URLSearchParams({ path: item.path })}`;
      link.download = item.name;
      link.onclick = e => e.stopPropagation();
    }
    tr.classList.toggle("selected", isSelected);
    tr.setAttribute("aria-selected", String(isSelected));
    rows.append(tr);
  }
}

function setRowSelection(row, checkbox, path, checked) {
  if (checked) selected.add(path);
  else selected.delete(path);
  row.classList.toggle("selected", checked);
  row.setAttribute("aria-selected", String(checked));
  checkbox.checked = checked;
  updateButtons();
}

function parentOf(path) {
  const i = path.lastIndexOf("/");
  return i < 0 ? "" : path.slice(0, i);
}

function updateButtons() {
  const count = selected.size;
  const one = count === 1;
  const selectedFiles = currentItems.filter(item => selected.has(item.path) && item.type === "file");
  const hasSelection = count > 0;
  browserBar.classList.toggle("selection-active", hasSelection);
  selectionBar.setAttribute("aria-hidden", String(!hasSelection));
  if (hasSelection) selectionBar.removeAttribute("inert");
  else selectionBar.setAttribute("inert", "");
  selectionSummary.textContent = `${count} item${one ? "" : "s"} selected`;
  downloadSelectedButton.disabled = selectedFiles.length === 0;
  archiveSelectedButton.disabled = count === 0;
  downloadSelectedButton.title = selectedFiles.length < count ? "Folders will be skipped" : "Download selected files";
  document.getElementById("rename").disabled = !one;
  document.getElementById("move").disabled = count === 0;
  document.getElementById("delete").disabled = count === 0;
  updateHeaderSelection();
}

function updateHeaderSelection() {
  const total = currentItems.length;
  const count = currentItems.reduce((value, item) => value + (selected.has(item.path) ? 1 : 0), 0);
  const allSelected = total > 0 && count === total;
  const partlySelected = count > 0 && count < total;
  selectionToggle.checked = allSelected;
  selectionToggle.indeterminate = partlySelected;
  selectionToggle.disabled = total === 0;
  selectionToggle.setAttribute("aria-label", total === 0
    ? "No items available"
    : allSelected ? "Deselect all items" : "Select all items");
}

function clearSelection() {
  selected.clear();
  renderRows();
  updateButtons();
}

function selectAllCurrentItems() {
  selected = new Set(currentItems.map(item => item.path));
  renderRows();
  updateButtons();
  setStatus(`${selected.size} item${selected.size === 1 ? "" : "s"} selected`);
}

function downloadSelectedFiles() {
  const files = currentItems.filter(item => selected.has(item.path) && item.type === "file");
  if (!files.length) return;

  for (const item of files) {
    const link = document.createElement("a");
    link.href = `${api}/download?${new URLSearchParams({ path: item.path })}`;
    link.download = item.name;
    link.hidden = true;
    document.body.append(link);
    link.click();
    link.remove();
  }

  const skippedCount = selected.size - files.length;
  const downloadLabel = `${files.length} file${files.length === 1 ? "" : "s"} download started`;
  setStatus(skippedCount ? `${downloadLabel}; ${skippedCount} folder${skippedCount === 1 ? "" : "s"} skipped` : downloadLabel);
}

async function activateSelectedItem() {
  if (selected.size !== 1) return;
  const path = [...selected][0];
  const item = currentItems.find(candidate => candidate.path === path);
  if (!item) return;
  if (item.type === "directory") {
    await navigateTo(item.path);
    return;
  }
  downloadSelectedFiles();
}

function isKeyboardShortcutTarget(target) {
  return target instanceof Element && target.closest("input, textarea, select, button, a, [contenteditable='true']") !== null;
}

function isFileDrag(event) {
  return [...(event.dataTransfer?.types || [])].includes("Files");
}

function clearBrowserUploadDropTarget() {
  browserPane.classList.remove("upload-drop-current");
  browserPane.querySelector(".upload-drop-target")?.classList.remove("upload-drop-target");
}

function updateBrowserUploadDropTarget(target) {
  clearBrowserUploadDropTarget();
  const row = target instanceof Element ? target.closest("tbody tr.directory-row[data-path]") : null;
  if (row && browserPane.contains(row)) {
    row.classList.add("upload-drop-target");
    return row.dataset.path;
  }

  browserPane.classList.add("upload-drop-current");
  return current;
}

function openUploadModal(destinationPath = current) {
  resetUploadStaging();
  uploadDestinationPath = (destinationPath || "").replace(/^\/+|\/+$/g, "");
  const destinationLabel = uploadDestinationPath ? `/${uploadDestinationPath}` : "/";
  uploadDestination.textContent = destinationLabel;
  uploadDestination.title = destinationLabel;
  uploadModal.hidden = false;
  uploadDrop.classList.remove("hot");
}

function closeUploadModal() {
  uploadModal.hidden = true;
  uploadDrop.classList.remove("hot");
  resetUploadStaging();
}

function displayBrowserPath(path) {
  return path ? `/${path}` : "/";
}

function combineBrowserPath(parent, name) {
  return parent ? `${parent}/${name}` : name;
}

function validItemName(name) {
  return name !== ""
    && name !== "."
    && name !== ".."
    && !name.includes("/")
    && !name.includes("\\");
}

function updateNewFolderDestination() {
  const name = newFolderName.value.trim();
  const validName = validItemName(name);
  const preview = newFolderDestinationPreview.parentElement;
  newFolderName.setCustomValidity(validName ? "" : "Enter a valid folder name.");
  newFolderDestinationPreview.textContent = validName
    ? displayBrowserPath(combineBrowserPath(newFolderParentPath, name))
    : "Enter a valid folder name";
  preview.classList.toggle("bad", !validName);
  confirmNewFolderButton.disabled = !validName;
}

function openNewFolderModal() {
  newFolderParentPath = current;
  const location = displayBrowserPath(newFolderParentPath);
  newFolderLocation.textContent = location;
  newFolderLocation.title = location;
  newFolderName.value = "New folder";
  updateNewFolderDestination();
  newFolderModal.hidden = false;
  newFolderName.focus();
  newFolderName.select();
}

function closeNewFolderModal() {
  newFolderModal.hidden = true;
  newFolderParentPath = "";
  newFolderName.value = "";
}

async function createNewFolder() {
  updateNewFolderDestination();
  if (confirmNewFolderButton.disabled) {
    newFolderName.reportValidity();
    return;
  }

  const path = combineBrowserPath(newFolderParentPath, newFolderName.value.trim());
  const op = await apiJson(`${api}/folders`, { path });
  closeNewFolderModal();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(op.path);
}

function updateRenameDestination() {
  const nextName = renameName.value.trim();
  const currentName = renameTargetPath.split("/").pop() || "";
  const validName = validItemName(nextName) && nextName !== currentName;
  const preview = renameDestinationPreview.parentElement;
  renameDestinationPreview.textContent = !validItemName(nextName)
    ? "Enter a valid item name"
    : nextName === currentName
      ? "Choose a different name"
      : displayBrowserPath(combineBrowserPath(parentOf(renameTargetPath), nextName));
  preview.classList.toggle("bad", !validName);
  confirmRenameButton.disabled = !validName;
}

function openRenameModal(path) {
  renameTargetPath = path;
  const label = displayBrowserPath(path);
  renameSource.textContent = label;
  renameSource.title = label;
  renameName.value = path.split("/").pop() || "";
  updateRenameDestination();
  renameModal.hidden = false;
  renameName.focus();
  renameName.select();
}

function closeRenameModal() {
  renameModal.hidden = true;
  renameTargetPath = "";
}

async function performRename() {
  updateRenameDestination();
  if (confirmRenameButton.disabled) return;
  const op = await apiJson(`${api}/rename`, { path: renameTargetPath, name: renameName.value.trim() });
  closeRenameModal();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(op.path);
}

function normalizedMoveDestination(value) {
  return value.trim().replace(/^\/+|\/+$/g, "");
}

function updateMoveDestination() {
  const rawDestination = moveDestination.value.trim();
  const destinationDirectory = normalizedMoveDestination(rawDestination);
  const segments = destinationDirectory.split("/").filter(Boolean);
  const validPath = !rawDestination.includes("\\")
    && segments.every(segment => segment !== "." && segment !== "..");
  const changesLocation = moveSourcePaths.some(path => parentOf(path) !== destinationDirectory);
  const validDestination = validPath && changesLocation;
  const preview = moveDestinationPreview.parentElement;
  moveDestinationPreview.textContent = !validPath
    ? "Enter a folder path without . or .."
    : !changesLocation
      ? "Choose a different folder"
      : displayBrowserPath(destinationDirectory);
  preview.classList.toggle("bad", !validDestination);
  confirmMoveButton.disabled = !validDestination;
}

function openMoveModal(paths) {
  if (!paths.length) return;
  moveSourcePaths = [...paths];
  document.getElementById("moveTitle").textContent = paths.length === 1 ? "Move item" : "Move items";
  const label = paths.length === 1 ? displayBrowserPath(paths[0]) : `${paths.length} selected items`;
  moveSelectionSummary.textContent = label;
  moveSelectionSummary.title = label;
  moveDestination.value = current;
  updateMoveDestination();
  moveModal.hidden = false;
  moveDestination.focus();
  moveDestination.select();
}

function closeMoveModal() {
  moveModal.hidden = true;
  moveSourcePaths = [];
}

async function performMove() {
  updateMoveDestination();
  if (confirmMoveButton.disabled) return;
  const destinationDirectory = normalizedMoveDestination(moveDestination.value);
  for (const sourcePath of moveSourcePaths) {
    const op = await apiJson(`${api}/move`, { sourcePath, destinationDirectory });
    trackQueuedOperation(op);
    setStatus(`Queued ${operationLabel(op.operation)}`);
    queueReloadFallback(op.path);
  }
  closeMoveModal();
}

function openDeleteModal(paths) {
  if (!paths.length) return;
  deleteSourcePaths = [...paths];
  document.getElementById("deleteTitle").textContent = paths.length === 1 ? "Delete item" : "Delete items";
  const label = paths.length === 1 ? displayBrowserPath(paths[0]) : `${paths.length} selected items`;
  deleteSelectionSummary.textContent = label;
  deleteSelectionSummary.title = label;
  deleteMessage.textContent = paths.length === 1
    ? "This item will be moved to the trash."
    : `These ${paths.length} items will be moved to the trash.`;
  deleteModal.hidden = false;
  confirmDeleteButton.focus();
}

function closeDeleteModal() {
  deleteModal.hidden = true;
  deleteSourcePaths = [];
}

async function performDelete() {
  const paths = [...deleteSourcePaths];
  if (!paths.length) return;
  const op = await apiJson(`${api}/delete`, { paths });
  closeDeleteModal();
  for (const path of paths) selected.delete(path);
  renderRows();
  updateButtons();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(op.path);
  queueTrashReloadFallback();
}

function normalizedArchiveName(value) {
  const name = value.trim();
  return name.toLowerCase().endsWith(".zip") ? name : `${name}.zip`;
}

function updateArchiveDestination() {
  const rawName = archiveName.value.trim();
  const validName = rawName !== ""
    && rawName !== "."
    && rawName !== ".."
    && !rawName.includes("/")
    && !rawName.includes("\\");
  archiveName.setCustomValidity(validName ? "" : "Enter a valid archive name.");
  archiveDestinationPreview.textContent = validName
    ? displayBrowserPath(combineBrowserPath(archiveDestinationDirectory, normalizedArchiveName(rawName)))
    : "Choose an archive name";
  confirmArchiveButton.disabled = !validName;
}

function openArchiveModal(paths) {
  if (!paths.length) return;
  archiveSourcePaths = [...paths];
  archiveDestinationDirectory = parentOf(paths[0]);
  const leaf = paths[0].split("/").pop() || "archive";
  archiveSelectionSummary.textContent = paths.length === 1
    ? leaf
    : `${paths.length} selected items`;
  archiveName.value = paths.length === 1 ? `${leaf}.zip` : "archive.zip";
  updateArchiveDestination();
  archiveModal.hidden = false;
  archiveName.focus();
  archiveName.select();
}

function closeArchiveModal() {
  archiveModal.hidden = true;
  archiveSourcePaths = [];
  archiveDestinationDirectory = "";
}

async function createArchive() {
  updateArchiveDestination();
  if (confirmArchiveButton.disabled) {
    archiveName.reportValidity();
    return;
  }

  const finalName = normalizedArchiveName(archiveName.value);
  archiveName.value = finalName;
  const destinationPath = combineBrowserPath(archiveDestinationDirectory, finalName);
  const op = await apiJson(`${api}/archive`, {
    paths: archiveSourcePaths,
    destinationPath
  });
  closeArchiveModal();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(destinationPath);
}

function updateExtractDestination() {
  const dedicatedFolder = extractToFolder.checked;
  const folderName = extractFolderName.value.trim();
  const validFolderName = folderName !== ""
    && folderName !== "."
    && folderName !== ".."
    && !folderName.includes("/")
    && !folderName.includes("\\");
  const parent = parentOf(extractArchivePath);
  extractFolderName.disabled = !dedicatedFolder;
  extractFolderName.setCustomValidity(dedicatedFolder && !validFolderName ? "Enter a valid folder name." : "");
  extractFolderPreview.textContent = validFolderName
    ? displayBrowserPath(combineBrowserPath(parent, folderName))
    : "Choose a folder name";
  confirmExtractButton.disabled = dedicatedFolder && !validFolderName;
}

function openExtractModal(path) {
  extractArchivePath = path;
  const leaf = path.split("/").pop() || "archive.zip";
  extractArchiveName.textContent = displayBrowserPath(path);
  extractArchiveName.title = displayBrowserPath(path);
  extractHerePreview.textContent = displayBrowserPath(parentOf(path));
  extractFolderName.value = leaf.replace(/\.zip$/i, "") || "archive";
  extractToFolder.checked = true;
  updateExtractDestination();
  extractModal.hidden = false;
  extractFolderName.focus();
  extractFolderName.select();
}

function closeExtractModal() {
  extractModal.hidden = true;
  extractArchivePath = "";
}

async function extractArchive() {
  updateExtractDestination();
  if (confirmExtractButton.disabled) {
    extractFolderName.reportValidity();
    return;
  }

  const createDestinationDirectory = extractToFolder.checked;
  const parent = parentOf(extractArchivePath);
  const destinationDirectory = createDestinationDirectory
    ? combineBrowserPath(parent, extractFolderName.value.trim())
    : parent;
  const op = await apiJson(`${api}/extract`, {
    path: extractArchivePath,
    destinationDirectory,
    createDestinationDirectory
  });
  closeExtractModal();
  trackQueuedOperation(op);
  setStatus(`Queued ${operationLabel(op.operation)}`);
  queueReloadFallback(destinationDirectory);
}

function uploadFileRelativePath(item) {
  const file = item.file || item;
  return (item.uploadRelativePath || file.webkitRelativePath || file.name).replaceAll("\\", "/");
}

function createStagedUploadItems(files) {
  const items = [];
  const folders = new Map();

  for (const file of files) {
    const relativePath = uploadFileRelativePath(file);
    const separator = relativePath.indexOf("/");
    if (separator < 0) {
      items.push({ name: file.name, isDirectory: false, files: [file] });
      continue;
    }

    const folderName = relativePath.slice(0, separator);
    let folder = folders.get(folderName);
    if (!folder) {
      folder = { name: folderName, isDirectory: true, files: [] };
      folders.set(folderName, folder);
      items.push(folder);
    }
    folder.files.push(file);
  }

  return items;
}

function addStagedUploadItems(items) {
  const existingPaths = new Set(stagedUploadItems.flatMap(item => item.files.map(uploadFileRelativePath)));
  let added = 0;

  for (const item of items) {
    const paths = item.files.map(uploadFileRelativePath);
    if (!paths.length || paths.some(path => existingPaths.has(path))) continue;
    for (const path of paths) existingPaths.add(path);
    stagedUploadItems.push({
      ...item,
      id: `staged-upload:${++stagedUploadSequence}`,
      size: item.files.reduce((total, fileItem) => total + (fileItem.file || fileItem).size, 0)
    });
    added++;
  }

  renderStagedUploads();
  if (!added && items.length) setStatus("Items already selected");
}

function removeStagedUploadItem(id) {
  stagedUploadItems = stagedUploadItems.filter(item => item.id !== id);
  renderStagedUploads();
}

function renderStagedUploads() {
  uploadStaging.innerHTML = "";
  uploadStaging.hidden = stagedUploadItems.length === 0;
  startUploadButton.disabled = stagedUploadItems.length === 0;

  for (const item of stagedUploadItems) {
    const row = document.createElement("div");
    row.className = "upload-staged-item";

    const icon = document.createElement("span");
    icon.className = item.isDirectory ? "folder-icon" : "file-icon";
    icon.setAttribute("aria-hidden", "true");

    const details = document.createElement("div");
    details.className = "upload-staged-details";
    const name = document.createElement("strong");
    name.className = "upload-staged-name";
    name.textContent = item.name;
    name.title = item.name;
    const size = document.createElement("span");
    size.className = "muted";
    size.textContent = fmtSize(item.size);
    details.append(name, size);

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "icon-button upload-remove";
    remove.setAttribute("aria-label", `Remove ${item.name}`);
    remove.title = `Remove ${item.name}`;
    remove.onclick = () => removeStagedUploadItem(item.id);
    const removeGlyph = document.createElement("span");
    removeGlyph.className = "upload-remove-glyph";
    removeGlyph.setAttribute("aria-hidden", "true");
    remove.append(removeGlyph);

    row.append(icon, details, remove);
    uploadStaging.append(row);
  }
}

function uploadStagedItems() {
  const files = stagedUploadItems.flatMap(item => item.files);
  if (!files.length) return;
  const destinationPath = uploadDestinationPath;
  closeUploadModal();
  beginUpload(files, destinationPath);
}

function resetUploadStaging() {
  stagedUploadItems = [];
  filesInput.value = "";
  folderInput.value = "";
  renderStagedUploads();
}

function beginUpload(files, destinationPath = current) {
  startUpload(files, destinationPath).catch(err => {
    if ((err.message || "") !== "upload_cancelled") {
      setStatus(err.message || "upload_failed");
    }
  });
}

function runAction(action) {
  action().catch(err => setStatus(err.message || "operation_failed"));
}

document.getElementById("refresh").onclick = () => load().catch(err => setStatus(err.message || "refresh_failed"));
openTrashButton.onclick = () => runAction(openTrashModal);
themeToggle.onclick = () => {
  const modes = ["system", "light", "dark"];
  const currentTheme = document.documentElement.dataset.theme || "system";
  applyTheme(modes[(modes.indexOf(currentTheme) + 1) % modes.length]);
};
function reloadFromFirstPage(historyMode) {
  return load(current, { token: "", cursorHistory: [], historyMode });
}

function applySearch() {
  clearTimeout(searchTimer);
  const value = searchInput.value.trim();
  clearSearchButton.hidden = !searchInput.value;
  if (value === search) return;
  search = value;
  reloadFromFirstPage("replace").catch(err => setStatus(err.message || "search_failed"));
}

searchInput.oninput = () => {
  clearSearchButton.hidden = !searchInput.value;
  clearTimeout(searchTimer);
  searchTimer = setTimeout(applySearch, 300);
};
searchInput.onkeydown = e => {
  if (e.key === "Enter") applySearch();
};
clearSearchButton.onclick = () => {
  searchInput.value = "";
  applySearch();
  searchInput.focus();
};
pageSizeSelect.onchange = () => {
  pageSize = Number(pageSizeSelect.value) || defaultPageSize;
  reloadFromFirstPage("push").catch(err => setStatus(err.message || "list_failed"));
};
for (const button of document.querySelectorAll("[data-sort]")) {
  button.onmouseenter = () => updateSortIndicators(button.dataset.sort);
  button.onmouseleave = () => updateSortIndicators();
  button.onfocus = () => updateSortIndicators(button.dataset.sort);
  button.onblur = () => updateSortIndicators();
  button.onclick = () => {
    const nextSort = button.dataset.sort;
    if (sort === nextSort) {
      direction = direction === "asc" ? "desc" : "asc";
    }
    else {
      sort = nextSort;
      direction = "asc";
    }
    updateSortIndicators(button.dataset.sort);
    reloadFromFirstPage("push").catch(err => setStatus(err.message || "list_failed"));
  };
}
previousPageButton.onclick = () => {
  if (!cursorHistory.length) return;
  const previousHistory = [...cursorHistory];
  const previousToken = previousHistory.pop() || "";
  load(current, {
    token: previousToken,
    cursorHistory: previousHistory,
    historyMode: "push"
  }).catch(err => setStatus(err.message || "list_failed"));
};
nextPageButton.onclick = () => {
  if (!nextContinuationToken) return;
  load(current, {
    token: nextContinuationToken,
    cursorHistory: [...cursorHistory, continuationToken],
    historyMode: "push"
  }).catch(err => setStatus(err.message || "list_failed"));
};
document.getElementById("newFolder").onclick = openNewFolderModal;
async function renameItem(path) {
  openRenameModal(path);
}

async function renameSelected() {
  await renameItem([...selected][0]);
}

async function moveItems(paths) {
  openMoveModal(paths);
}

async function moveSelected() {
  await moveItems([...selected]);
}

async function deleteItems(paths) {
  openDeleteModal(paths);
}

async function deleteSelected() {
  await deleteItems([...selected]);
}

downloadSelectedButton.onclick = downloadSelectedFiles;
archiveSelectedButton.onclick = () => openArchiveModal([...selected]);
document.getElementById("rename").onclick = () => runAction(renameSelected);
document.getElementById("move").onclick = () => runAction(moveSelected);
document.getElementById("delete").onclick = () => runAction(deleteSelected);
selectionToggle.onchange = () => {
  if (selectionToggle.checked) selectAllCurrentItems();
  else clearSelection();
};

toggleOperations.onclick = toggleOperationsPanel;
clearOperationsButton.onclick = clearOperationHistory;
cancelOperationsButton.onclick = () => runAction(cancelAllOperations);
confirmNewFolderButton.onclick = () => runAction(createNewFolder);
document.getElementById("closeNewFolder").onclick = closeNewFolderModal;
document.getElementById("cancelNewFolder").onclick = closeNewFolderModal;
document.querySelector("[data-close-new-folder]").onclick = closeNewFolderModal;
newFolderName.oninput = updateNewFolderDestination;
newFolderName.onkeydown = e => {
  if (e.key === "Enter" && !confirmNewFolderButton.disabled) {
    e.preventDefault();
    runAction(createNewFolder);
  }
};
document.getElementById("upload").onclick = () => openUploadModal(current);
document.getElementById("chooseFiles").onclick = () => filesInput.click();
document.getElementById("chooseFolder").onclick = () => folderInput.click();
startUploadButton.onclick = uploadStagedItems;
document.getElementById("closeUpload").onclick = closeUploadModal;
document.querySelector("[data-close-upload]").onclick = closeUploadModal;
document.getElementById("closeTrash").onclick = closeTrashModal;
document.getElementById("doneTrash").onclick = closeTrashModal;
document.querySelector("[data-close-trash]").onclick = closeTrashModal;
emptyTrashButton.onclick = () => openPurgeModal(trashItems.map(item => item.id), true);
confirmPurgeButton.onclick = () => runAction(performPurge);
document.getElementById("closePurge").onclick = closePurgeModal;
document.getElementById("cancelPurge").onclick = closePurgeModal;
document.querySelector("[data-close-purge]").onclick = closePurgeModal;
confirmRestoreElsewhereButton.onclick = () => runAction(performRestoreElsewhere);
document.getElementById("closeRestoreElsewhere").onclick = closeRestoreElsewhereModal;
document.getElementById("cancelRestoreElsewhere").onclick = closeRestoreElsewhereModal;
document.querySelector("[data-close-restore-elsewhere]").onclick = closeRestoreElsewhereModal;
restoreElsewhereDestination.oninput = updateRestoreElsewhereDestination;
restoreElsewhereDestination.onkeydown = e => {
  if (e.key === "Enter" && !confirmRestoreElsewhereButton.disabled) {
    e.preventDefault();
    runAction(performRestoreElsewhere);
  }
};
confirmRenameButton.onclick = () => runAction(performRename);
document.getElementById("closeRename").onclick = closeRenameModal;
document.getElementById("cancelRename").onclick = closeRenameModal;
document.querySelector("[data-close-rename]").onclick = closeRenameModal;
renameName.oninput = updateRenameDestination;
renameName.onkeydown = e => {
  if (e.key === "Enter" && !confirmRenameButton.disabled) {
    e.preventDefault();
    runAction(performRename);
  }
};
confirmMoveButton.onclick = () => runAction(performMove);
document.getElementById("closeMove").onclick = closeMoveModal;
document.getElementById("cancelMove").onclick = closeMoveModal;
document.querySelector("[data-close-move]").onclick = closeMoveModal;
moveDestination.oninput = updateMoveDestination;
moveDestination.onkeydown = e => {
  if (e.key === "Enter" && !confirmMoveButton.disabled) {
    e.preventDefault();
    runAction(performMove);
  }
};
confirmDeleteButton.onclick = () => runAction(performDelete);
document.getElementById("closeDelete").onclick = closeDeleteModal;
document.getElementById("cancelDelete").onclick = closeDeleteModal;
document.querySelector("[data-close-delete]").onclick = closeDeleteModal;
confirmArchiveButton.onclick = () => runAction(createArchive);
document.getElementById("closeArchive").onclick = closeArchiveModal;
document.querySelector("[data-close-archive]").onclick = closeArchiveModal;
archiveName.oninput = updateArchiveDestination;
archiveName.onkeydown = e => {
  if (e.key === "Enter" && !confirmArchiveButton.disabled) {
    e.preventDefault();
    runAction(createArchive);
  }
};
confirmExtractButton.onclick = () => runAction(extractArchive);
document.getElementById("closeExtract").onclick = closeExtractModal;
document.querySelector("[data-close-extract]").onclick = closeExtractModal;
for (const radio of document.querySelectorAll("input[name='extractDestinationMode']")) {
  radio.onchange = updateExtractDestination;
}
extractFolderName.oninput = updateExtractDestination;
extractFolderName.onkeydown = e => {
  if (e.key === "Enter" && !confirmExtractButton.disabled) {
    e.preventDefault();
    runAction(extractArchive);
  }
};
document.addEventListener("keydown", e => {
  if (e.key === "Escape") {
    if (!newFolderModal.hidden) {
      e.preventDefault();
      closeNewFolderModal();
    }
    else if (!uploadModal.hidden) {
      e.preventDefault();
      closeUploadModal();
    }
    else if (!purgeModal.hidden) {
      e.preventDefault();
      closePurgeModal();
    }
    else if (!restoreElsewhereModal.hidden) {
      e.preventDefault();
      closeRestoreElsewhereModal();
    }
    else if (!trashModal.hidden) {
      e.preventDefault();
      closeTrashModal();
    }
    else if (!renameModal.hidden) {
      e.preventDefault();
      closeRenameModal();
    }
    else if (!moveModal.hidden) {
      e.preventDefault();
      closeMoveModal();
    }
    else if (!deleteModal.hidden) {
      e.preventDefault();
      closeDeleteModal();
    }
    else if (!archiveModal.hidden) {
      e.preventDefault();
      closeArchiveModal();
    }
    else if (!extractModal.hidden) {
      e.preventDefault();
      closeExtractModal();
    }
    else if (selected.size) {
      e.preventDefault();
      clearSelection();
    }
    return;
  }

  if (isModalOpen() || isKeyboardShortcutTarget(e.target)) return;

  if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === "a") {
    e.preventDefault();
    selectAllCurrentItems();
  }
  else if (e.key === "Delete" && selected.size) {
    e.preventDefault();
    runAction(deleteSelected);
  }
  else if (e.key === "F2" && selected.size === 1) {
    e.preventDefault();
    runAction(renameSelected);
  }
  else if (e.key === "Enter" && selected.size === 1) {
    e.preventDefault();
    runAction(activateSelectedItem);
  }
});
filesInput.onchange = e => {
  addStagedUploadItems(createStagedUploadItems([...e.target.files]));
  e.target.value = "";
};
folderInput.onchange = e => {
  addStagedUploadItems(createStagedUploadItems([...e.target.files]));
  e.target.value = "";
};

browserPane.addEventListener("dragenter", e => {
  if (!isFileDrag(e) || isModalOpen()) return;
  e.preventDefault();
  browserFileDragDepth++;
  updateBrowserUploadDropTarget(e.target);
});
browserPane.addEventListener("dragover", e => {
  if (!isFileDrag(e) || isModalOpen()) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "copy";
  updateBrowserUploadDropTarget(e.target);
});
browserPane.addEventListener("dragleave", e => {
  if (!isFileDrag(e) || isModalOpen()) return;
  browserFileDragDepth = Math.max(0, browserFileDragDepth - 1);
  if (browserFileDragDepth === 0) clearBrowserUploadDropTarget();
});
browserPane.addEventListener("drop", async e => {
  if (!isFileDrag(e) || isModalOpen()) return;
  e.preventDefault();
  e.stopPropagation();
  const destinationPath = updateBrowserUploadDropTarget(e.target);
  browserFileDragDepth = 0;
  clearBrowserUploadDropTarget();
  openUploadModal(destinationPath);
  try {
    const items = await collectDroppedUploadItems(e.dataTransfer);
    addStagedUploadItems(items);
  }
  catch (err) {
    setStatus(err.message || "drop_failed");
  }
});

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
    const items = await collectDroppedUploadItems(e.dataTransfer);
    addStagedUploadItems(items);
  }
  catch (err) {
    setStatus(err.message || "drop_failed");
  }
});

async function collectDroppedUploadItems(dataTransfer) {
  const items = [...(dataTransfer?.items || [])];
  const entries = items
    .map(item => typeof item.webkitGetAsEntry === "function" ? item.webkitGetAsEntry() : null)
    .filter(Boolean);

  if (!entries.length) {
    return createStagedUploadItems([...(dataTransfer?.files || [])]);
  }

  const stagedItems = [];
  for (const entry of entries) {
    const files = [];
    await walkDroppedEntry(entry, "", files);
    stagedItems.push({
      name: entry.name,
      isDirectory: entry.isDirectory,
      files
    });
  }
  return stagedItems;
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

async function startUpload(files, destinationPath = current) {
  if (!files.length) return;
  const generation = cancelGeneration;
  const entries = files.map((item, index) => {
    const file = item.file || item;
    const relativePath = uploadFileRelativePath(item);
    const path = (destinationPath ? `${destinationPath}/` : "") + relativePath;
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
      updateOperation(entry.id, { status: "running" });
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
      updateOperation(entry.id, { status: "running", done: entry.size || 1, total: entry.size || 1 });
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
  queueReloadFallback(destinationPath);
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
          status: "running",
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

  const currentOperation = operations.get(id);
  const size = totalBytes || currentOperation?.total || 1;
  trackOperation({
    id,
    kind: "upload",
    path,
    status: completed ? "done" : "running",
    done: completed ? size : Math.max(receivedBytes || 0, currentOperation?.done || 0),
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
    readNavigationState();
    setupEvents();
    loadTrash().catch(() => null);
    window.onpopstate = e => {
      clearTimeout(searchTimer);
      readNavigationState(e.state);
      load(current, {
        token: continuationToken,
        cursorHistory,
        historyMode: "none"
      }).catch(err => setStatus(err.message || "list_failed"));
    };
    return load(current, {
      token: continuationToken,
      cursorHistory,
      historyMode: "replace"
    });
  })
  .catch(err => setStatus(err.message));
