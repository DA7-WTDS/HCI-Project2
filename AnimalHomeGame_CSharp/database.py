"""
database.py — CRUD layer for children's game records and teacher accounts.

Two JSON files live next to this script:
  • children_db.json  — one record per child/student
  • teachers_db.json  — one record per teacher

Child schema:
{
    "id":           "User_1",
    "username":     "User_1",
    "role":         "child",
    "age":          null,
    "high_score":   null,          # best completion time in seconds (lower = better)
    "games_played": 0,
    "last_played":  null,
    "last_emotion": "neutral",
    "registered_at": "2025-01-01T00:00:00"
}

Teacher schema:
{
    "id":           "Teacher_1",
    "username":     "Teacher_1",
    "role":         "teacher",
    "age":          null,
    "registered_at": "2025-01-01T00:00:00"
}
"""

import json
import os
import glob as _glob
from datetime import datetime

_DIR = os.path.dirname(os.path.abspath(__file__))
CHILDREN_DB_PATH = os.path.join(_DIR, "children_db.json")
TEACHERS_DB_PATH = os.path.join(_DIR, "teachers_db.json")


# ── helpers ──────────────────────────────────────────────────────────────────

def _now() -> str:
    return datetime.now().isoformat(timespec="seconds")


def _load(path: str) -> dict:
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    return {}


def _save(path: str, data: dict) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)


# ── CHILDREN — CREATE ─────────────────────────────────────────────────────────

def register_child(child_id: str, age: int | None = None) -> dict:
    """
    Called automatically when ai_vision.py saves a new face photo.
    Creates a fresh record only if child_id does not already exist.
    Returns the (possibly existing) record.
    """
    db = _load(CHILDREN_DB_PATH)
    if child_id not in db:
        record = {
            "id":            child_id,
            "username":      child_id,
            "role":          "child",
            "age":           age,
            "high_score":    None,
            "games_played":  0,
            "last_played":   None,
            "last_emotion":  "neutral",
            "registered_at": _now(),
        }
        db[child_id] = record
        _save(CHILDREN_DB_PATH, db)
        print(f"[DB] Registered new child: {child_id}")
    return db[child_id]


# ── CHILDREN — READ ───────────────────────────────────────────────────────────

def get_child(child_id: str) -> dict | None:
    """Return a single child record, or None if not found."""
    return _load(CHILDREN_DB_PATH).get(child_id)


def get_all_children() -> list[dict]:
    """Return all children sorted by username."""
    db = _load(CHILDREN_DB_PATH)
    return sorted(db.values(), key=lambda r: r["username"].lower())


# ── CHILDREN — UPDATE ─────────────────────────────────────────────────────────

def update_seen(child_id: str, emotion: str) -> None:
    """
    Called every time ai_vision.py recognises a face.
    Updates last_played timestamp and last_emotion.
    """
    db = _load(CHILDREN_DB_PATH)
    if child_id not in db:
        register_child(child_id)
        db = _load(CHILDREN_DB_PATH)
    db[child_id]["last_played"]  = _now()
    db[child_id]["last_emotion"] = emotion
    _save(CHILDREN_DB_PATH, db)


def update_score(child_id: str, new_time_seconds: float) -> dict:
    """
    Called when a child finishes a game.
    Updates high_score only if new_time is LOWER (faster) than the stored best.
    Also increments games_played and sets last_played.
    Returns a dict with keys:
        - record: the updated child record
        - is_new_best: bool
        - old_score: the previous high_score (or None)
    """
    db = _load(CHILDREN_DB_PATH)
    if child_id not in db:
        register_child(child_id)
        db = _load(CHILDREN_DB_PATH)

    record    = db[child_id]
    old_score = record["high_score"]

    record["games_played"] += 1
    record["last_played"]   = _now()

    is_new_best = (old_score is None) or (new_time_seconds < old_score)
    if is_new_best:
        record["high_score"] = round(new_time_seconds, 2)

    _save(CHILDREN_DB_PATH, db)
    print(
        f"[DB] Score update — {child_id}: "
        f"{old_score}s → {new_time_seconds}s "
        f"{'✓ NEW BEST' if is_new_best else '(no change)'}"
    )
    return {"record": record, "is_new_best": is_new_best, "old_score": old_score}


def set_child_age(child_id: str, age: int) -> dict | None:
    """Update a child's age. Returns the updated record or None if not found."""
    db = _load(CHILDREN_DB_PATH)
    if child_id not in db:
        print(f"[DB] set_child_age failed — {child_id} not found.")
        return None
    db[child_id]["age"] = age
    _save(CHILDREN_DB_PATH, db)
    print(f"[DB] Updated age for {child_id} → {age}")
    return db[child_id]


def rename_child(child_id: str, new_username: str) -> dict | None:
    """
    Updates the username field while keeping the auto-generated id unchanged.
    Returns the updated record, or None if child_id doesn't exist.
    """
    db = _load(CHILDREN_DB_PATH)
    if child_id not in db:
        print(f"[DB] Rename failed — {child_id} not found.")
        return None
    db[child_id]["username"] = new_username.strip()
    _save(CHILDREN_DB_PATH, db)
    print(f"[DB] Renamed {child_id} → '{new_username}'")
    return db[child_id]


# ── CHILDREN — DELETE ─────────────────────────────────────────────────────────

def delete_child(child_id: str, faces_dir: str | None = None) -> bool:
    """
    Deletes a child's record from the database.
    If faces_dir is provided, also removes their photo and clears DeepFace cache.
    Returns True if the child existed and was deleted, False otherwise.
    """
    db = _load(CHILDREN_DB_PATH)
    if child_id not in db:
        print(f"[DB] Delete failed — {child_id} not found.")
        return False

    del db[child_id]
    _save(CHILDREN_DB_PATH, db)
    print(f"[DB] Deleted child: {child_id}")

    if faces_dir:
        photo_path = os.path.join(faces_dir, f"{child_id}.jpg")
        if os.path.exists(photo_path):
            os.remove(photo_path)
            print(f"[DB] Removed face photo: {photo_path}")
        for pkl in _glob.glob(os.path.join(faces_dir, "*.pkl")):
            try:
                os.remove(pkl)
            except OSError:
                pass

    return True


# ── TEACHERS — CREATE ─────────────────────────────────────────────────────────

def register_teacher(teacher_id: str, age: int | None = None) -> dict:
    """
    Creates a fresh teacher record only if teacher_id does not already exist.
    Returns the (possibly existing) record.
    """
    db = _load(TEACHERS_DB_PATH)
    if teacher_id not in db:
        record = {
            "id":            teacher_id,
            "username":      teacher_id,
            "role":          "teacher",
            "age":           age,
            "registered_at": _now(),
        }
        db[teacher_id] = record
        _save(TEACHERS_DB_PATH, db)
        print(f"[DB] Registered new teacher: {teacher_id}")
    return db[teacher_id]


# ── TEACHERS — READ ───────────────────────────────────────────────────────────

def get_teacher(teacher_id: str) -> dict | None:
    """Return a single teacher record, or None if not found."""
    return _load(TEACHERS_DB_PATH).get(teacher_id)


def get_all_teachers() -> list[dict]:
    """Return all teachers sorted by username."""
    db = _load(TEACHERS_DB_PATH)
    return sorted(db.values(), key=lambda r: r["username"].lower())


# ── TEACHERS — UPDATE ─────────────────────────────────────────────────────────

def rename_teacher(teacher_id: str, new_username: str) -> dict | None:
    """Update a teacher's username. Returns the updated record or None if not found."""
    db = _load(TEACHERS_DB_PATH)
    if teacher_id not in db:
        print(f"[DB] Rename failed — {teacher_id} not found.")
        return None
    db[teacher_id]["username"] = new_username.strip()
    _save(TEACHERS_DB_PATH, db)
    print(f"[DB] Renamed {teacher_id} → '{new_username}'")
    return db[teacher_id]


def set_teacher_age(teacher_id: str, age: int) -> dict | None:
    """Update a teacher's age. Returns the updated record or None if not found."""
    db = _load(TEACHERS_DB_PATH)
    if teacher_id not in db:
        print(f"[DB] set_teacher_age failed — {teacher_id} not found.")
        return None
    db[teacher_id]["age"] = age
    _save(TEACHERS_DB_PATH, db)
    print(f"[DB] Updated age for {teacher_id} → {age}")
    return db[teacher_id]


# ── TEACHERS — DELETE ─────────────────────────────────────────────────────────

def delete_teacher(teacher_id: str) -> bool:
    """
    Deletes a teacher's record from the database.
    Returns True if the teacher existed and was deleted, False otherwise.
    """
    db = _load(TEACHERS_DB_PATH)
    if teacher_id not in db:
        print(f"[DB] Delete failed — {teacher_id} not found.")
        return False
    del db[teacher_id]
    _save(TEACHERS_DB_PATH, db)
    print(f"[DB] Deleted teacher: {teacher_id}")
    return True


# ── ADMIN DASHBOARD HELPER ────────────────────────────────────────────────────

def get_all_users() -> list[dict]:
    """
    Returns all children and teachers merged into one list, sorted by username.
    Useful for the admin dashboard.
    """
    all_users = get_all_children() + get_all_teachers()
    return sorted(all_users, key=lambda r: r["username"].lower())
