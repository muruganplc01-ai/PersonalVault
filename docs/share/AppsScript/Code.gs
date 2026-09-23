/**
 * Personal Vault - Share Account one-time-view backend.
 *
 * Deploy this as a Google Apps Script Web App (script.google.com -> New project ->
 * paste this file's contents over Code.gs -> Deploy -> New deployment -> Web app ->
 * "Execute as: Me", "Who has access: Anyone" -> paste the resulting .../exec URL into
 * docs/share/index.html's APPS_SCRIPT_URL constant). See README.md's "Sharing an
 * account (one-time link)" section for the full walkthrough.
 *
 * Runs under the vault owner's own Google account regardless of who calls it - that's
 * what makes a real one-time view possible without the recipient ever needing a Google
 * account or any Drive access of their own. Given a Drive file id, this serves that
 * file's bytes exactly once (base64-encoded, since a Web App's response body has to be
 * text) and deletes it (moves to Trash) in the same request, inside a lock so two
 * simultaneous requests for the same id can't both succeed.
 *
 * The Drive file itself is never shared "anyone with the link" - it stays completely
 * private. Only this script (running as its owner) can ever read it, and only until
 * the first successful read consumes it.
 */
function doGet(e) {
  var id = e && e.parameter && e.parameter.id;
  if (!id) return goneResponse();

  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(10000);
  } catch (lockError) {
    // Another request is already handling this (or some other) id - rather than risk
    // two requests both reading the same not-yet-deleted file, treat a lock timeout the
    // same as "gone". The other request either succeeds or fails on its own; this one
    // simply doesn't get to serve the file.
    return goneResponse();
  }

  try {
    var file;
    try {
      file = DriveApp.getFileById(id);
    } catch (notFoundError) {
      // Already trashed by a previous request (consumed, expired, or manually
      // revoked from the app) - this is the normal steady state once a share is gone.
      return goneResponse();
    }

    var bytes = file.getBlob().getBytes();
    var base64 = Utilities.base64Encode(bytes);

    // Trash rather than permanently delete - recoverable from Drive's Trash for 30
    // days if something ever goes wrong, same "non-destructive by default" spirit as
    // the rest of this app (see VaultStorage's atomic writes, GoogleDriveSync's
    // rolling revision backups, etc).
    file.setTrashed(true);

    return ContentService.createTextOutput(base64).setMimeType(ContentService.MimeType.TEXT);
  } finally {
    lock.releaseLock();
  }
}

function goneResponse() {
  return ContentService.createTextOutput("GONE").setMimeType(ContentService.MimeType.TEXT);
}
