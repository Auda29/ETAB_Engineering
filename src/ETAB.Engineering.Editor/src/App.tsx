import { useCallback, useEffect, useState } from "react";
import { editorApi } from "./api";
import { BottomPanel } from "./components/BottomPanel";
import { Inspector } from "./components/Inspector";
import { MachineCanvas } from "./components/MachineCanvas";
import { Palette } from "./components/Palette";
import { ProjectTree } from "./components/ProjectTree";
import { TopBar } from "./components/TopBar";
import type { EtabNode, EtabProjectDocument, NodeKind, PreviewResponse, RelationKind, ValidationResponse } from "./model";
import { createNode } from "./modelFactory";

type Notice = { tone: "success" | "error" | "info"; text: string };

export default function App() {
  const [document, setDocument] = useState<EtabProjectDocument>();
  const [path, setPath] = useState("");
  const [exampleProjectPath, setExampleProjectPath] = useState("");
  const [sessionReady, setSessionReady] = useState(false);
  const [supportsNativeFileDialogs, setSupportsNativeFileDialogs] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<string>();
  const [validation, setValidation] = useState<ValidationResponse>();
  const [preview, setPreview] = useState<PreviewResponse>();
  const [busy, setBusy] = useState(false);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [generateBusy, setGenerateBusy] = useState(false);
  const [generationRoot, setGenerationRoot] = useState("");
  const [integrateProject, setIntegrateProject] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [notice, setNotice] = useState<Notice>();

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
      setIntegrateProject(false);
      setValidation(result.validation);
      setSelectedNodeId(result.document.nodes[0]?.id);
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

    let requestedPath = path.trim();
    if (supportsNativeFileDialogs) {
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
    }

    await loadProject(requestedPath);
  }, [dirty, loadProject, path, supportsNativeFileDialogs]);

  const newProject = useCallback(async () => {
    if (dirty && !window.confirm("Discard unsaved editor changes and create a new project?")) return;
    setBusy(true);
    setNotice({ tone: "info", text: "Creating a new project…" });
    try {
      const result = await editorApi.createNew();
      setDocument(result.document);
      setPath("");
      setGenerationRoot("");
      setIntegrateProject(false);
      setValidation(result.validation);
      setSelectedNodeId(result.document.nodes[0]?.id);
      setPreview(undefined);
      setDirty(true);
      setNotice({ tone: "info", text: "New project created from the minimal template. Save it to choose a location." });
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }, [dirty]);

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

  const saveProject = useCallback(async (saveAs = false) => {
    if (!document) return;
    let targetPath = path.trim();
    if (saveAs || !targetPath) {
      if (!supportsNativeFileDialogs) {
        setNotice({ tone: "info", text: "Enter the new project path above, then select Save." });
        return;
      }

      setBusy(true);
      setNotice({ tone: "info", text: "Choose where to save the ETAB project…" });
      try {
        const selection = await editorApi.chooseSaveProject(`${document.project.name}.etab.json`);
        if (selection.canceled || !selection.path) {
          setNotice(undefined);
          return;
        }
        targetPath = selection.path;
      } catch (error) {
        setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
        return;
      } finally {
        setBusy(false);
      }
    }

    const pathChanged = targetPath !== path;
    setBusy(true);
    setNotice({ tone: "info", text: "Saving project…" });
    try {
      const result = await editorApi.save(targetPath, document);
      setPath(result.path);
      if (pathChanged) setGenerationRoot(result.projectRoot);
      setValidation(result.validation);
      setDirty(false);
      setPreview(undefined);
      setNotice({ tone: result.validation.isValid ? "success" : "info", text: result.validation.isValid ? "Project saved and validated" : `Draft saved with ${result.validation.issues.length} validation issues` });
    } catch (error) {
      setNotice({ tone: "error", text: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }, [document, path, supportsNativeFileDialogs]);

  useEffect(() => {
    const saveShortcut = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        void saveProject(false);
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

  const addNode = useCallback((kind: NodeKind) => {
    if (!document) return;
    const node = createNode(kind, document);
    updateDocument((draft) => {
      draft.nodes.push(node);
      const index = draft.nodes.length - 1;
      draft.layout.nodes.push({
        nodeId: node.id,
        x: 80 + (index % 4) * 270,
        y: 80 + Math.floor(index / 4) * 175,
      });
    });
    setSelectedNodeId(node.id);
  }, [document, updateDocument]);

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

  const changeGenerationRoot = useCallback((value: string) => {
    setGenerationRoot(value);
    setPreview(undefined);
  }, []);

  const changeIntegrateProject = useCallback((value: boolean) => {
    setIntegrateProject(value);
    setPreview(undefined);
  }, []);

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

  if (!document) {
    return (
      <div className="startup-screen">
        <div className="startup-card">
          <div className="brand__mark">ET</div>
          <h1>ETAB Engineering</h1>
          <p>{notice?.text ?? (sessionReady ? "Create a logical machine model or open an existing ETAB project." : "Connecting to the local engineering service…")}</p>
          {sessionReady && (
            <>
              {!supportsNativeFileDialogs && (
                <input
                  className="filebar__path startup-path"
                  value={path}
                  onChange={(event) => setPath(event.target.value)}
                  onKeyDown={(event) => event.key === "Enter" && void openProject()}
                  placeholder="C:\\Path\\Project.etab.json"
                  spellCheck={false}
                />
              )}
              <div className="startup-actions">
                <button className="button button--primary" onClick={() => void newProject()} disabled={busy}>New Project</button>
                <button className="button button--secondary" onClick={() => void openProject()} disabled={busy}>Open Project</button>
              </div>
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
        onPathChange={(value) => { setPath(value); setPreview(undefined); }}
        onNew={() => void newProject()}
        onOpen={() => void openProject()}
        onSave={() => void saveProject(false)}
        onSaveAs={() => void saveProject(true)}
        supportsNativeFileDialogs={supportsNativeFileDialogs}
        busy={busy}
        dirty={dirty}
        projectName={document.project.displayName}
        validation={validation}
      />
      <div className="workspace">
        <aside className="sidebar">
          <Palette onAdd={addNode} />
          <ProjectTree document={document} selectedNodeId={selectedNodeId} onSelect={setSelectedNodeId} />
        </aside>
        <MachineCanvas
          document={document}
          selectedNodeId={selectedNodeId}
          onSelect={setSelectedNodeId}
          onMoveNode={moveNode}
          onAddRelation={addRelation}
          onUpdateRelation={updateRelation}
          onDeleteRelation={deleteRelation}
        />
        <Inspector document={document} selectedNodeId={selectedNodeId} updateDocument={updateDocument} updateNode={updateNode} deleteNode={deleteNode} />
      </div>
      <BottomPanel validation={validation} preview={preview} previewBusy={previewBusy} generateBusy={generateBusy} generationRoot={generationRoot} integrateProject={integrateProject} dirty={dirty} onGenerationRootChange={changeGenerationRoot} onIntegrateProjectChange={changeIntegrateProject} onPreview={refreshPreview} onGenerate={generateProject} onIssueSelect={selectIssue} />
      {notice && <button className={`notice notice--${notice.tone}`} onClick={() => setNotice(undefined)}>{notice.text}<span>×</span></button>}
    </div>
  );
}
