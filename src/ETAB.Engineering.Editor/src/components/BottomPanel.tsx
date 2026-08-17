import { useEffect, useMemo, useState } from "react";
import type { PreviewResponse, ValidationResponse } from "../model";

type Tab = "validation" | "preview";

export function BottomPanel({
  validation,
  preview,
  previewBusy,
  generateBusy,
  generationRoot,
  integrateProject,
  dirty,
  onPreview,
  onGenerate,
  onIssueSelect,
}: {
  validation?: ValidationResponse;
  preview?: PreviewResponse;
  previewBusy: boolean;
  generateBusy: boolean;
  generationRoot: string;
  integrateProject: boolean;
  dirty: boolean;
  onPreview: () => void;
  onGenerate: () => void;
  onIssueSelect: (path: string) => void;
}) {
  const [tab, setTab] = useState<Tab>("validation");
  const [selectedArtifact, setSelectedArtifact] = useState<string>();
  useEffect(() => {
    if (preview?.artifacts.length) setSelectedArtifact(preview.artifacts[0].relativePath);
  }, [preview]);
  const artifact = useMemo(
    () => preview?.artifacts.find((item) => item.relativePath === selectedArtifact),
    [preview, selectedArtifact],
  );
  const selectedDocument = selectedArtifact === "__manifest"
    ? preview?.manifest
    : selectedArtifact === "__project"
      ? preview?.projectFile
      : selectedArtifact === "__task"
        ? preview?.taskFile
        : selectedArtifact === "__integration-manifest"
          ? preview?.projectIntegrationManifest
          : undefined;
  const canGenerate = Boolean(
    preview?.confirmationToken &&
    !preview.hasConflicts &&
    !previewBusy &&
    !generateBusy &&
    !dirty &&
    generationRoot.trim(),
  );

  return (
    <section className="bottom-panel">
      <nav className="bottom-panel__tabs">
        <button className={tab === "validation" ? "active" : ""} onClick={() => setTab("validation")}>
          Validation <span className={validation?.isValid ? "count count--valid" : "count count--invalid"}>{validation?.issues.length ?? "–"}</span>
        </button>
        <button className={tab === "preview" ? "active" : ""} onClick={() => setTab("preview")}>
          Generation preview <span data-testid="preview-count" className="count">{preview?.artifacts.length ?? "–"}</span>
        </button>
        <div className="bottom-panel__actions">
          <span className="generation-root-label">Target</span>
          <code className="generation-root" aria-label="PLC target root" title={generationRoot}>{generationRoot}</code>
          <span className={`generation-project-toggle ${integrateProject ? "is-linked" : ""}`} title={integrateProject ? "The linked TwinCAT PLC project is updated automatically" : "No TwinCAT PLC project is linked"}>
            {integrateProject ? "✓ .plcproj linked" : "Model only"}
          </span>
          <button data-testid="preview-button" className="button button--secondary button--compact" onClick={() => { setTab("preview"); onPreview(); }} disabled={previewBusy || generateBusy || !validation?.isValid || !generationRoot.trim()}>
            {previewBusy ? "Planning…" : "Refresh preview"}
          </button>
          <button data-testid="generate-button" className="button button--primary button--compact" onClick={() => { setTab("preview"); onGenerate(); }} disabled={!canGenerate} title={dirty ? "Save the ETAB model before generation" : "Write the confirmed generation plan"}>
            {generateBusy ? "Generating…" : "Generate"}
          </button>
        </div>
      </nav>

      <div className="bottom-panel__content">
        {tab === "validation" && (
          <div className="validation-pane">
            {validation?.isValid ? (
              <div className="validation-success"><span>✓</span><div><strong>Model is valid</strong><p>Schema and semantic validation from ETAB.Engineering.Core passed.</p></div></div>
            ) : validation ? (
              <div className="issue-list">
                {validation.issues.map((issue, index) => (
                  <button key={`${issue.code}-${issue.path}-${index}`} className="issue-row" onClick={() => onIssueSelect(issue.path)}>
                    <span className="issue-row__code">{issue.code}</span>
                    <code>{issue.path}</code>
                    <span>{issue.message}</span>
                  </button>
                ))}
              </div>
            ) : <div className="panel-placeholder">Open a project to start validation.</div>}
          </div>
        )}

        {tab === "preview" && (
          preview ? (
            <div className="preview-grid">
              <div className="preview-plan">
                <div className="preview-summary">
                  <span className={`status-pill ${preview.hasConflicts ? "status-pill--invalid" : "status-pill--valid"}`}>{preview.hasConflicts ? "Conflicts" : "Safe plan"}</span>
                  <span>{preview.artifacts.length} artifacts</span>
                  <span>{preview.generatedRoot}</span>
                </div>
                <div className="change-list">
                  {preview.issues.map((issue, index) => (
                    <button key={`${issue.code}-${issue.path}-${index}`} className="preview-issue" onClick={() => onIssueSelect(issue.path)}>
                      <strong>{issue.code}</strong><code>{issue.path}</code><span>{issue.message}</span>
                    </button>
                  ))}
                  {preview.changes.map((change) => (
                    <button key={`${change.sourceModelId}-${change.artifactKind}`} className={`change-row change-row--${change.changeKind}`} onClick={() => setSelectedArtifact(change.relativePath)}>
                      <span>{change.changeKind}</span><code>{change.relativePath}</code><small>{change.artifactKind}</small>
                    </button>
                  ))}
                  {preview.manifest && <button className={`change-row change-row--${preview.manifest.changeKind}`} onClick={() => setSelectedArtifact("__manifest")}><span>{preview.manifest.changeKind}</span><code>{preview.manifest.relativePath}</code><small>manifest</small></button>}
                  {preview.projectFile && <button className={`change-row change-row--${preview.projectFile.changeKind}`} onClick={() => setSelectedArtifact("__project")}><span>{preview.projectFile.changeKind}</span><code>{preview.projectFile.relativePath}</code><small>PLC project</small></button>}
                  {preview.taskFile && <button className={`change-row change-row--${preview.taskFile.changeKind}`} onClick={() => setSelectedArtifact("__task")}><span>{preview.taskFile.changeKind}</span><code>{preview.taskFile.relativePath}</code><small>runtime task</small></button>}
                  {preview.projectIntegrationManifest && <button className={`change-row change-row--${preview.projectIntegrationManifest.changeKind}`} onClick={() => setSelectedArtifact("__integration-manifest")}><span>{preview.projectIntegrationManifest.changeKind}</span><code>{preview.projectIntegrationManifest.relativePath}</code><small>project manifest</small></button>}
                </div>
              </div>
              <div className="artifact-viewer">
                <div className="artifact-viewer__header">
                  <strong>{selectedDocument?.relativePath ?? artifact?.relativePath ?? "Select an artifact"}</strong>
                  {artifact && <span>GUID {artifact.twinCatGuid}</span>}
                </div>
                <pre data-testid="artifact-content">{selectedDocument?.content ?? artifact?.content}</pre>
              </div>
            </div>
          ) : <div className="panel-placeholder">Run a preview to see the read-only generation plan and complete TwinCAT XML.</div>
        )}
      </div>
    </section>
  );
}
