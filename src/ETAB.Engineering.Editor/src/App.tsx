import { useCallback, useEffect, useState } from "react";
import { editorApi } from "./api";
import { BottomPanel } from "./components/BottomPanel";
import { Inspector } from "./components/Inspector";
import { MachineCanvas } from "./components/MachineCanvas";
import { Palette } from "./components/Palette";
import { ProjectTree } from "./components/ProjectTree";
import { TopBar } from "./components/TopBar";
import type { EtabNode, EtabProjectDocument, NodeKind, PreviewResponse, ValidationResponse } from "./model";
import { createNode } from "./modelFactory";

type Notice = { tone: "success" | "error" | "info"; text: string };

export default function App() {
  const [document, setDocument] = useState<EtabProjectDocument>();
  const [path, setPath] = useState("examples/BrushMachine.reference.etab.json");
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

  const openProject = useCallback(async (targetPath?: string, skipDirtyCheck = false) => {
    const requestedPath = targetPath ?? path;
    if (!skipDirtyCheck && dirty && !window.confirm("Discard unsaved editor changes and open another project?")) return;
    setBusy(true);
    setNotice({ tone: "info", text: "Opening project…" });
    try {
      const result = await editorApi.open(requestedPath);
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
  }, [dirty, path]);

  useEffect(() => {
    const controller = new AbortController();
    editorApi.session(controller.signal)
      .then((session) => {
        setPath(session.exampleProjectPath);
        return openProject(session.exampleProjectPath, true);
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setNotice({ tone: "error", text: `Service unavailable: ${error instanceof Error ? error.message : String(error)}` });
      });
    return () => controller.abort();
  }, []); // Load the reference project exactly once for the initial editor session.

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
    setBusy(true);
    setNotice({ tone: "info", text: "Saving project…" });
    try {
      const result = await editorApi.save(path, document);
      setPath(result.path);
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
          <p>{notice?.text ?? "Connecting to the local engineering service…"}</p>
          {notice?.tone === "error" && <button className="button button--primary" onClick={() => openProject(path, true)}>Retry</button>}
        </div>
      </div>
    );
  }

  return (
    <div className="app-shell">
      <TopBar path={path} onPathChange={(value) => { setPath(value); setPreview(undefined); }} onOpen={() => openProject()} onSave={saveProject} busy={busy} dirty={dirty} projectName={document.project.displayName} validation={validation} />
      <div className="workspace">
        <aside className="sidebar">
          <Palette onAdd={addNode} />
          <ProjectTree document={document} selectedNodeId={selectedNodeId} onSelect={setSelectedNodeId} />
        </aside>
        <MachineCanvas document={document} selectedNodeId={selectedNodeId} onSelect={setSelectedNodeId} onMoveNode={moveNode} />
        <Inspector document={document} selectedNodeId={selectedNodeId} updateDocument={updateDocument} updateNode={updateNode} deleteNode={deleteNode} />
      </div>
      <BottomPanel validation={validation} preview={preview} previewBusy={previewBusy} generateBusy={generateBusy} generationRoot={generationRoot} integrateProject={integrateProject} dirty={dirty} onGenerationRootChange={changeGenerationRoot} onIntegrateProjectChange={changeIntegrateProject} onPreview={refreshPreview} onGenerate={generateProject} onIssueSelect={selectIssue} />
      {notice && <button className={`notice notice--${notice.tone}`} onClick={() => setNotice(undefined)}>{notice.text}<span>×</span></button>}
    </div>
  );
}
