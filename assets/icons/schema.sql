-- Onion ProcOperator icon database (SQLite)
-- Rebuilt from icon-src/ by build_icon_db.py. Do not hand-edit icons.db; edit icon-src/ and rebuild.

PRAGMA foreign_keys = ON;

CREATE TABLE icon (
    id           INTEGER PRIMARY KEY,
    key          TEXT    NOT NULL UNIQUE,   -- stable lookup key, e.g. 'app', 'tray.connected'
    name         TEXT    NOT NULL,
    name_th      TEXT,
    category     TEXT    NOT NULL,          -- 'app' | 'status' | ... (free text, used for pickers)
    description  TEXT,
    ico          BLOB    NOT NULL,          -- multi-size .ico (for NotifyIcon / Window.Icon)
    sha256       TEXT    NOT NULL,          -- hash of all SVG variants: use as a cache key
    version      INTEGER NOT NULL DEFAULT 1,
    created_utc  TEXT    NOT NULL
);

CREATE TABLE icon_svg (
    icon_id  INTEGER NOT NULL REFERENCES icon(id) ON DELETE CASCADE,
    variant  TEXT    NOT NULL CHECK (variant IN ('full', 'small', 'tiny')),
    svg      TEXT    NOT NULL,
    PRIMARY KEY (icon_id, variant)
);

CREATE TABLE icon_raster (
    icon_id  INTEGER NOT NULL REFERENCES icon(id) ON DELETE CASCADE,
    size     INTEGER NOT NULL,              -- square px
    png      BLOB    NOT NULL,
    PRIMARY KEY (icon_id, size)
);

CREATE INDEX ix_icon_category ON icon(category);
