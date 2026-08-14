import type { ValidationResponse } from "../model";

interface Props {
  path: string;
  onPathChange: (path: string) => void;
  onNew: () => void;
  onOpen: () => void;
  onSave: () => void;
  onSaveAs: () => void;
  supportsNativeFileDialogs: boolean;
  busy: boolean;
  dirty: boolean;
  projectName?: string;
  validation?: ValidationResponse;
  theme: "dark" | "light";
  onThemeToggle: () => void;
}

export function TopBar({
  path,
  onPathChange,
  onNew,
  onOpen,
  onSave,
  onSaveAs,
  supportsNativeFileDialogs,
  busy,
  dirty,
  projectName,
  validation,
  theme,
  onThemeToggle,
}: Props) {
  const issueCount = validation?.issues.length ?? 0;
  return (
    <header className="topbar">
      <div className="brand" aria-label="ETAB Engineering">
        <div className="brand__mark">ET</div>
        <div>
          <div className="brand__name">ETAB Engineering</div>
          <div className="brand__phase">Visual Editor · Phase 3</div>
        </div>
      </div>

      <div className="filebar">
        <span className="filebar__label">Project</span>
        <input
          data-testid="project-path"
          className="filebar__path"
          value={path}
          onChange={(event) => onPathChange(event.target.value)}
          onKeyDown={(event) => !supportsNativeFileDialogs && event.key === "Enter" && onOpen()}
          placeholder="No project file selected"
          readOnly={supportsNativeFileDialogs}
          spellCheck={false}
        />
        <button className="button button--secondary" onClick={onNew} disabled={busy}>
          New
        </button>
        <button data-testid="open-project" className="button button--secondary" onClick={onOpen} disabled={busy}>
          Open
        </button>
        <button data-testid="save-project" className="button button--primary" onClick={onSave} disabled={busy || !projectName}>
          Save
        </button>
        <button className="button button--secondary" onClick={onSaveAs} disabled={busy || !projectName}>
          Save As
        </button>
      </div>

      <div className="topbar__status">
        <button
          className="theme-toggle"
          type="button"
          title={`Switch to ${theme === "dark" ? "light" : "dark"} mode`}
          aria-label={`Switch to ${theme === "dark" ? "light" : "dark"} mode`}
          aria-pressed={theme === "light"}
          onClick={onThemeToggle}
        >
          <span aria-hidden="true">{theme === "dark" ? "☀" : "☾"}</span>
        </button>
        {projectName && <span className="project-name">{projectName}</span>}
        {dirty && <span className="status-pill status-pill--dirty">Unsaved</span>}
        {validation && (
          <span
            data-testid="validation-status"
            className={`status-pill ${validation.isValid ? "status-pill--valid" : "status-pill--invalid"}`}
          >
            {validation.isValid ? "Valid" : `${issueCount} issue${issueCount === 1 ? "" : "s"}`}
          </span>
        )}
      </div>
    </header>
  );
}
