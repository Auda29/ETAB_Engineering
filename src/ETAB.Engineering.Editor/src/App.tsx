import { useCallback, useEffect, useLayoutEffect, useState } from "react";
import { editorApi } from "./api";
import {
  areaViewForGroup,
  createUniqueAreaName,
  getLayoutGroups,
  groupNameFromAreaView,
  nodeGroup,
  nodeMatchesArea,
  type AreaView,
} from "./areaModel";
import { BottomPanel } from "./components/BottomPanel";
import { Inspector } from "./components/Inspector";
import { MachineCanvas } from "./components/MachineCanvas";
import { Palette } from "./components/Palette";
import { ProjectTree } from "./components/ProjectTree";
import { TopBar } from "./components/TopBar";
import type { EtabNode, EtabProjectDocument, NodeKind, PreviewResponse, RelationKind, ValidationResponse } from "./model";
import { createCommand, createNode } from "./modelFactory";

type Notice = { tone: "success" | "error" | "info"; text: string };
type Theme = "dark" | "light";

export default function App() {
  const [theme, setTheme] = useState<Theme>(() => {
    const saved = window.localStorage.getItem("etab-theme");
    if (saved === "dark" || saved === "light") return saved;
    return window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
  });
  const [document, setDocument] = useState<EtabProjectDocument>();
  const [path, setPath] = useState("");
  const [exampleProjectPath, setExampleProjectPath] = useState("");
  const [sessionReady, setSessionReady] = useState(false);
  const [supportsNativeFileDialogs, setSupportsNativeFileDialogs] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<string>();
  const [activeAreaView, setActiveAreaView] = useState<AreaView>("all");
  const [validation, setValidation] = useState<ValidationResponse>();
  const [preview, setPreview] = useState<PreviewResponse>();
  const [busy, setBusy] = useState(false);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [generateBusy, setGenerateBusy] = useState(false);
  const [generationRoot, setGenerationRoot] = useState("");
  const [integrateProject, setIntegrateProject] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [notice, setNotice] = useState<Notice>();
  const [inspectorFocus, setInspectorFocus] = useState<{ nodeId: string; tab: "commands"; requestId: string }>();

  const loadProject = useCallback(async (requestedPath: string) => {
    if (!requestedPath.trim()) {
      setNotice({ tone: "error", text: "Select an ETAB project file first" });
      return;
    }
    setBusy(true);
    setNotice({ tone: "info", text: "Opening project…" });
    try {
      const result = await editorApi.open(requestedPath.trim());
      setDocument(result.document);
      setPath(result.path);
      setGenerationRoot(result.projectRoot);
      setIntegrateProject(Boolean(result.document.project.twinCAT.plcProject));
      setValidation(result.validation);
      setSelectedNodeId(result.document.nodes[0]?.id);
      setActiveAreaView("all");
      setPreview(undefined);
      setDirty(false);
      setNotice({ tone: "success", text: `Opened ${result.document.project.displayName}` });
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }, []);

  const openProject = useCallback(async () => {
    if (dirty && !window.confirm("Discard unsaved editor changes and open another project?")) return;

    if (!supportsNativeFileDialogs) {
      setNotice({ tone: "error", text: "Use the Windows desktop application to select an ETAB project file." });
      return;
    }

    let requestedPath: string;
    setBusy(true);
    setNotice({ tone: "info", text: "Choose an ETAB project file…" });
    try {
      const selection = await editorApi.chooseOpenProject();
      if (selection.canceled || !selection.path) {
        setNotice(undefined);
        return;
      }
      requestedPath = selection.path;
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
      return;
    } finally {
      setBusy(false);
    }

    await loadProject(requestedPath);
  }, [dirty, loadProject, supportsNativeFileDialogs]);

  const connectPlcProject = useCallback(async () => {
    if (dirty && !window.confirm("Discard unsaved editor changes and connect another TwinCAT PLC project?")) return;
    if (!supportsNativeFileDialogs) {
      setNotice({ tone: "error", text: "Use the Windows desktop application to select a TwinCAT .plcproj file." });
      return;
    }

    setBusy(true);
    setNotice({ tone: "info", text: "Choose the empty TwinCAT .plcproj file…" });
    try {
      const result = await editorApi.connectPlcProject();
      if (result.canceled || !result.project) {
        setNotice(undefined);
        return;
      }

      const connected = result.project;
      setDocument(connected.document);
      setPath(connected.path);
      setGenerationRoot(connected.projectRoot);
      setIntegrateProject(true);
      setValidation(connected.validation);
      setSelectedNodeId(connected.document.nodes[0]?.id);
      setActiveAreaView("all");
      setPreview(undefined);
      setDirty(false);
      setNotice({
        tone: "success",
        text: connected.created
          ? `Connected ${connected.plcProjectPath} and created ${connected.path}`
          : `Opened the ETAB model linked to ${connected.plcProjectPath}`,
      });
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }, [dirty, supportsNativeFileDialogs]);

  useEffect(() => {
    const controller = new AbortController();
    editorApi.session(controller.signal)
      .then((session) => {
        setExampleProjectPath(session.exampleProjectPath);
        setSupportsNativeFileDialogs(session.supportsNativeFileDialogs);
        setSessionReady(true);
        setNotice(undefined);
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setNotice({ tone: "error", text: `Service unavailable: ${error instanceof Error ? error.message : String(error)}` });
      });
    return () => controller.abort();
  }, []); // Establish editor capabilities without opening a project implicitly.

  useLayoutEffect(() => {
    window.document.documentElement.dataset.theme = theme;
    window.localStorage.setItem("etab-theme", theme);
  }, [theme]);

  useEffect(() => {
    const preventPageWheelZoom = (event: WheelEvent) => {
      if (event.ctrlKey) event.preventDefault();
    };
    const preventPageKeyboardZoom = (event: KeyboardEvent) => {
      if (!event.ctrlKey || !["+", "-", "=", "0"].includes(event.key)) return;
      event.preventDefault();
    };
    window.document.addEventListener("wheel", preventPageWheelZoom, { passive: false });
    window.document.addEventListener("keydown", preventPageKeyboardZoom);
    return () => {
      window.document.removeEventListener("wheel", preventPageWheelZoom);
      window.document.removeEventListener("keydown", preventPageKeyboardZoom);
    };
  }, []);

  useEffect(() => {
    if (!document) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      editorApi.validate(document, controller.signal)
        .then(setValidation)
        .catch((error) => {
          if (error instanceof DOMException && error.name === "AbortError") return;
          setNotice({ tone: "error", text: `Validation failed: ${error instanceof Error ? error.message : String(error)}` });
        });
    }, 260);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [document]);

  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => {
      if (!dirty) return;
      event.preventDefault();
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const saveProject = useCallback(async () => {
    if (!document) return;
    const targetPath = path.trim();
    if (!targetPath) {
      setNotice({ tone: "error", text: "Connect a TwinCAT PLC project before saving." });
      return;
    }

    setBusy(true);
    setNotice({ tone: "info", text: "Saving project…" });
    try {
      const result = await editorApi.save(targetPath, document);
      setPath(result.path);
      setGenerationRoot(result.projectRoot);
      setValidation(result.validation);
      setDirty(false);
      setPreview(undefined);
      setNotice({ tone: result.validation.isValid ? "success" : "info", text: result.validation.isValid ? "Project saved and validated" : `Draft saved with ${result.validation.issues.length} validation issues` });
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }, [document, path]);

  useEffect(() => {
    const saveShortcut = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        void saveProject();
      }
    };
    window.addEventListener("keydown", saveShortcut);
    return () => window.removeEventListener("keydown", saveShortcut);
  }, [saveProject]);

  const updateDocument = useCallback((mutation: (draft: EtabProjectDocument) => void) => {
    setDocument((current) => {
      if (!current) return current;
      const next = structuredClone(current);
      mutation(next);
      return next;
    });
    setDirty(true);
    setPreview(undefined);
  }, []);

  const updateNode = useCallback((nodeId: string, mutation: (node: EtabNode) => void) => {
    updateDocument((draft) => {
      const node = draft.nodes.find((item) => item.id === nodeId);
      if (node) mutation(node);
    });
  }, [updateDocument]);

  const renameNode = useCallback((nodeId: string, displayName: string, name: string, symbolStem: string) => {
    updateNode(nodeId, (node) => {
      node.displayName = displayName.trim();
      node.name = name.trim();
      node.symbolStem = symbolStem.trim();
    });
  }, [updateNode]);

  const addNode = useCallback((kind: NodeKind, position?: { x: number; y: number }, group?: string) => {
    if (!document) return;
    const node = createNode(kind, document);
    updateDocument((draft) => {
      draft.nodes.push(node);
      const index = draft.nodes.length - 1;
      draft.layout.nodes.push({
        nodeId: node.id,
        x: position?.x ?? 80 + (index % 4) * 270,
        y: position?.y ?? 80 + Math.floor(index / 4) * 175,
        ...(group ? { group } : {}),
      });
    });
    setSelectedNodeId(node.id);
  }, [document, updateDocument]);

  const createArea = useCallback((displayName: string) => {
    if (!document || !displayName.trim()) return undefined;
    const name = createUniqueAreaName(displayName.trim(), getLayoutGroups(document));
    updateDocument((draft) => {
      draft.layout.groups = getLayoutGroups(draft);
      draft.layout.groups.push({ name, displayName: displayName.trim() });
    });
    setActiveAreaView(areaViewForGroup(name));
    setSelectedNodeId(undefined);
    return name;
  }, [document, updateDocument]);

  const renameArea = useCallback((name: string, displayName: string) => {
    if (!displayName.trim()) return;
    updateDocument((draft) => {
      draft.layout.groups = getLayoutGroups(draft);
      const group = draft.layout.groups.find((item) => item.name.toLowerCase() === name.toLowerCase());
      if (group) group.displayName = displayName.trim();
    });
  }, [updateDocument]);

  const deleteArea = useCallback((name: string) => {
    const area = document ? getLayoutGroups(document).find((group) => group.name.toLowerCase() === name.toLowerCase()) : undefined;
    if (!area || !window.confirm(`Remove the ${area.displayName} area? Its nodes will become unassigned; no nodes or relationships will be deleted.`)) return;
    updateDocument((draft) => {
      draft.layout.groups = getLayoutGroups(draft).filter((group) => group.name.toLowerCase() !== name.toLowerCase());
      draft.layout.nodes.forEach((layout) => {
        if (layout.group?.toLowerCase() === name.toLowerCase()) delete layout.group;
      });
    });
    setActiveAreaView("unassigned");
  }, [document, updateDocument]);

  const moveNodeToArea = useCallback((nodeId: string, group?: string) => {
    updateDocument((draft) => {
      let layout = draft.layout.nodes.find((item) => item.nodeId === nodeId);
      if (!layout) {
        layout = { nodeId, x: 80, y: 80 };
        draft.layout.nodes.push(layout);
      }
      if (group) layout.group = group;
      else delete layout.group;
    });
    setActiveAreaView(group ? areaViewForGroup(group) : "unassigned");
  }, [updateDocument]);

  const selectTreeNode = useCallback((nodeId?: string) => {
    setSelectedNodeId(nodeId);
    if (!document || !nodeId) return;
    const group = nodeGroup(document, nodeId);
    setActiveAreaView(group ? areaViewForGroup(group) : "unassigned");
  }, [document]);

  const changeActiveAreaView = useCallback((view: AreaView) => {
    setActiveAreaView(view);
    if (document && selectedNodeId && !nodeMatchesArea(document, selectedNodeId, view)) {
      setSelectedNodeId(undefined);
    }
  }, [document, selectedNodeId]);

  const addCommand = useCallback((nodeId: string) => {
    updateDocument((draft) => {
      const node = draft.nodes.find((item) => item.id === nodeId);
      if (!node?.generate.commandEnum) return;
      node.commands.push(createCommand(node.commands));
    });
    setSelectedNodeId(nodeId);
    setInspectorFocus({ nodeId, tab: "commands", requestId: crypto.randomUUID() });
  }, [updateDocument]);

  const deleteNode = useCallback((nodeId: string) => {
    const node = document?.nodes.find((item) => item.id === nodeId);
    if (!node || !window.confirm(`Delete ${node.displayName} and all of its relationships?`)) return;
    updateDocument((draft) => {
      draft.nodes = draft.nodes.filter((item) => item.id !== nodeId);
      draft.relations = draft.relations.filter((relation) => relation.sourceNodeId !== nodeId && relation.targetNodeId !== nodeId);
      draft.layout.nodes = draft.layout.nodes.filter((layout) => layout.nodeId !== nodeId);
    });
    setSelectedNodeId(document?.nodes.find((item) => item.id !== nodeId)?.id);
  }, [document, updateDocument]);

  const moveNode = useCallback((nodeId: string, x: number, y: number) => {
    updateDocument((draft) => {
      let layout = draft.layout.nodes.find((item) => item.nodeId === nodeId);
      if (!layout) {
        layout = { nodeId, x, y };
        draft.layout.nodes.push(layout);
      }
      layout.x = x;
      layout.y = y;
    });
  }, [updateDocument]);

  const addRelation = useCallback((sourceNodeId: string, targetNodeId: string, kind: RelationKind, label?: string) => {
    updateDocument((draft) => {
      draft.relations.push({
        id: crypto.randomUUID().toLowerCase(),
        kind,
        sourceNodeId,
        targetNodeId,
        ...(label?.trim() ? { label: label.trim() } : {}),
      });
    });
  }, [updateDocument]);

  const updateRelation = useCallback((relationId: string, kind: RelationKind, label?: string) => {
    updateDocument((draft) => {
      const relation = draft.relations.find((item) => item.id === relationId);
      if (!relation) return;
      relation.kind = kind;
      if (label?.trim()) relation.label = label.trim();
      else delete relation.label;
    });
  }, [updateDocument]);

  const deleteRelation = useCallback((relationId: string) => {
    updateDocument((draft) => {
      draft.relations = draft.relations.filter((relation) => relation.id !== relationId);
    });
  }, [updateDocument]);

  const refreshPreview = useCallback(async () => {
    if (!document) return;
    setPreviewBusy(true);
    try {
      const result = await editorApi.preview(
        document,
        path,
        generationRoot,
        integrateProject,
      );
      setPreview(result);
      setValidation(result.validation);
      setNotice({ tone: result.hasConflicts ? "error" : "success", text: result.hasConflicts ? "Preview contains conflicts" : `Preview planned ${result.artifacts.length} artifacts without writing` });
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setPreviewBusy(false);
    }
  }, [document, generationRoot, integrateProject, path]);

  const generateProject = useCallback(async () => {
    if (!document || !preview?.confirmationToken) return;
    if (dirty) {
      setNotice({ tone: "error", text: "Save the ETAB model before generating PLC files" });
      return;
    }
    const changedArtifacts = preview.changes.filter((change) => change.changeKind !== "unchanged").length;
    const projectNote = integrateProject ? " The configured .plcproj is included." : "";
    if (!window.confirm(
      `Write ${changedArtifacts} planned artifact changes to:\n${generationRoot}\n\n${projectNote} This operation uses the displayed conflict-protected plan.`,
    )) return;

    setGenerateBusy(true);
    setNotice({ tone: "info", text: "Generating PLC files…" });
    try {
      const result = await editorApi.generate(
        document,
        path,
        generationRoot,
        integrateProject,
        preview.confirmationToken,
      );
      if (!result.success) {
        throw new Error(result.issues.map((issue) => `[${issue.code}] ${issue.message}`).join("\n") || "Generation failed");
      }
      const refreshed = await editorApi.preview(
        document,
        path,
        generationRoot,
        integrateProject,
      );
      setPreview(refreshed);
      setValidation(refreshed.validation);
      setNotice({
        tone: "success",
        text: `Generation completed: ${result.created} created, ${result.updated} updated, ${result.renamed} renamed, ${result.deleted} deleted`,
      });
    } catch (error) {
      setPreview(undefined);
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setGenerateBusy(false);
    }
  }, [dirty, document, generationRoot, integrateProject, path, preview]);

  const selectIssue = useCallback((issuePath: string) => {
    const match = /^\/nodes\/(\d+)/.exec(issuePath);
    if (match && document) setSelectedNodeId(document.nodes[Number(match[1])]?.id);
  }, [document]);

  const toggleTheme = useCallback(() => {
    setTheme((current) => current === "dark" ? "light" : "dark");
  }, []);

  if (!document) {
    return (
      <div className="startup-screen">
        <button
          className="theme-toggle startup-theme-toggle"
          type="button"
          title={`Switch to ${theme === "dark" ? "light" : "dark"} mode`}
          aria-label={`Switch to ${theme === "dark" ? "light" : "dark"} mode`}
          onClick={toggleTheme}
        ><span aria-hidden="true">{theme === "dark" ? "☀" : "☾"}</span></button>
        <div className="startup-card">
          <div className="brand__mark">ET</div>
          <h1>ETAB Engineering</h1>
          <p>{notice?.text ?? (sessionReady ? "Start with the empty PLC project you created in TwinCAT." : "Connecting to the local engineering service…")}</p>
          {sessionReady && (
            <>
              <ol className="startup-steps">
                <li>Create an empty PLC project in TwinCAT.</li>
                <li>Select its <code>.plcproj</code> file here.</li>
                <li>ETAB assigns the model, output folders and project integration automatically.</li>
              </ol>
              <div className="startup-actions">
                <button className="button button--primary" onClick={() => void connectPlcProject()} disabled={busy || !supportsNativeFileDialogs}>Connect TwinCAT PLC Project</button>
                <button className="button button--secondary" onClick={() => void openProject()} disabled={busy || !supportsNativeFileDialogs}>Open Existing ETAB Model</button>
              </div>
              {!supportsNativeFileDialogs && <p className="startup-desktop-hint">Native file selection is available in the Windows desktop application.</p>}
              {exampleProjectPath && (
                <button className="link-button startup-example" onClick={() => void loadProject(exampleProjectPath)} disabled={busy}>
                  Open BrushMachine example
                </button>
              )}
            </>
          )}
          {!sessionReady && notice?.tone === "error" && (
            <button className="button button--primary" onClick={() => window.location.reload()}>Retry</button>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="app-shell">
      <TopBar
        path={path}
        onConnectPlc={() => void connectPlcProject()}
        onOpen={() => void openProject()}
        onSave={() => void saveProject()}
        busy={busy}
        dirty={dirty}
        projectName={document.project.displayName}
        validation={validation}
        theme={theme}
        onThemeToggle={toggleTheme}
      />
      <div className="workspace">
        <aside className="sidebar">
          <Palette onAdd={(kind) => addNode(kind, undefined, groupNameFromAreaView(activeAreaView))} />
          <ProjectTree
            document={document}
            selectedNodeId={selectedNodeId}
            activeAreaView={activeAreaView}
            onSelect={selectTreeNode}
            onSelectArea={changeActiveAreaView}
            onRenameArea={renameArea}
            onDeleteArea={deleteArea}
          />
        </aside>
        <MachineCanvas
          document={document}
          selectedNodeId={selectedNodeId}
          onSelect={setSelectedNodeId}
          onMoveNode={moveNode}
          onAddNode={addNode}
          onRenameNode={renameNode}
          onAddCommand={addCommand}
          activeAreaView={activeAreaView}
          onActiveAreaViewChange={changeActiveAreaView}
          onCreateArea={createArea}
          onRenameArea={renameArea}
          onDeleteArea={deleteArea}
          onMoveNodeToArea={moveNodeToArea}
          onAddRelation={addRelation}
          onUpdateRelation={updateRelation}
          onDeleteRelation={deleteRelation}
        />
        <Inspector document={document} selectedNodeId={selectedNodeId} requestedTab={inspectorFocus} updateDocument={updateDocument} updateNode={updateNode} deleteNode={deleteNode} />
      </div>
      <BottomPanel validation={validation} preview={preview} previewBusy={previewBusy} generateBusy={generateBusy} generationRoot={generationRoot} integrateProject={integrateProject} dirty={dirty} onPreview={refreshPreview} onGenerate={generateProject} onIssueSelect={selectIssue} />
      {notice && <button className={`notice notice--${notice.tone}`} onClick={() => setNotice(undefined)}>{notice.text}<span>×</span></button>}
    </div>
  );
}
