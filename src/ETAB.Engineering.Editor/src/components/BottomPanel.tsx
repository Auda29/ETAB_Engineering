import { useEffect, useMemo, useState } from "react";
import type { PreviewResponse, ValidationResponse } from "../model";

type Tab = "validation" | "preview";

export function BottomPanel({
  validation,
  preview,
  previewBusy,
  onPreview,
  onIssueSelect,
}: {
  validation?: ValidationResponse;
  preview?: PreviewResponse;
  previewBusy: boolean;
  onPreview: () => void;
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
          <button data-testid="preview-button" className="button button--primary button--compact" onClick={() => { setTab("preview"); onPreview(); }} disabled={previewBusy || !validation?.isValid}>
            {previewBusy ? "Planning…" : "Refresh preview"}
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
                  {preview.changes.map((change) => (
                    <button key={`${change.sourceModelId}-${change.artifactKind}`} className={`change-row change-row--${change.changeKind}`} onClick={() => setSelectedArtifact(change.relativePath)}>
                      <span>{change.changeKind}</span><code>{change.relativePath}</code><small>{change.artifactKind}</small>
                    </button>
                  ))}
                  {preview.manifest && <button className={`change-row change-row--${preview.manifest.changeKind}`} onClick={() => setSelectedArtifact("__manifest")}><span>{preview.manifest.changeKind}</span><code>{preview.manifest.relativePath}</code><small>manifest</small></button>}
                </div>
              </div>
              <div className="artifact-viewer">
                <div className="artifact-viewer__header">
                  <strong>{selectedArtifact === "__manifest" ? preview.manifest?.relativePath : artifact?.relativePath ?? "Select an artifact"}</strong>
                  {artifact && <span>GUID {artifact.twinCatGuid}</span>}
                </div>
                <pre data-testid="artifact-content">{selectedArtifact === "__manifest" ? preview.manifest?.content : artifact?.content}</pre>
              </div>
            </div>
          ) : <div className="panel-placeholder">Run a preview to see the read-only generation plan and complete TwinCAT XML.</div>
        )}
      </div>
    </section>
  );
}
