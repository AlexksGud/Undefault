(function () {
  "use strict";

  var POLL_MS = 2000;
  var EMPTY_COPY =
    "No sessions yet. That is normal while a player is idle. Windows only lists an app after playback has started. Press play in the player you want to control; this list will refresh.";

  var PLAYBACK_LABELS = {
    Playing: "Playing",
    Paused: "Paused",
    Stopped: "Stopped",
    Unknown: "Unknown"
  };

  var COMMAND_LABELS = {
    pause: "pause",
    resume: "resume",
    next: "next track",
    previous: "previous track",
    duck: "duck volume",
    restore_volume: "restore volume"
  };

  var els = {
    banner: document.getElementById("host-banner"),
    sessionsList: document.getElementById("sessions-list"),
    sessionsUpdated: document.getElementById("sessions-updated"),
    selectionStatus: document.getElementById("selection-status"),
    testOutcome: document.getElementById("test-outcome"),
    pause: document.getElementById("btn-pause"),
    resume: document.getElementById("btn-resume"),
    cs2Status: document.getElementById("cs2-status"),
    cs2Install: document.getElementById("btn-cs2-install"),
    cs2Outcome: document.getElementById("cs2-outcome"),
    flow: document.getElementById("btn-flow"),
    focus: document.getElementById("btn-focus"),
    presetStatus: document.getElementById("preset-status"),
    lastCommand: document.getElementById("last-command")
  };

  var state = {
    sessionsPayload: null,
    sessionsError: null,
    sessionsFingerprint: "",
    selectedAppId: null,
    selectNote: null,
    lastCommand: null,
    preset: null,
    busySelect: false,
    busyTest: false,
    busyCs2: false,
    busyPreset: false,
    sessionsToken: 0
  };

  document.addEventListener("click", function (event) {
    var sessionButton = event.target.closest("[data-app-id]");
    if (sessionButton && els.sessionsList.contains(sessionButton)) {
      event.preventDefault();
      selectSession(sessionButton.getAttribute("data-app-id"));
      return;
    }
  });

  els.pause.addEventListener("click", function () {
    runTest("pause");
  });
  els.resume.addEventListener("click", function () {
    runTest("resume");
  });
  els.cs2Install.addEventListener("click", installCs2);
  els.flow.addEventListener("click", function () {
    setPreset("Flow");
  });
  els.focus.addEventListener("click", function () {
    setPreset("Focus");
  });

  poll();
  setInterval(poll, POLL_MS);

  function poll() {
    if (document.hidden) {
      return;
    }
    refreshSessions();
    refreshLastCommand();
    refreshCs2();
    refreshPreset();
  }

  function refreshSessions() {
    var token = ++state.sessionsToken;
    return request("GET", "/music/sessions").then(
      function (result) {
        if (token !== state.sessionsToken) {
          return;
        }
        if (result.ok) {
          state.sessionsPayload = result.data || {};
          state.sessionsError = null;
          if (!state.busySelect) {
            applyHostSelection(state.sessionsPayload);
          }
          hideBanner();
        } else {
          state.sessionsError = describeHostError(result, "/music/sessions");
          showBanner(state.sessionsError);
        }
        renderSessions();
        renderSelection();
      },
      function () {
        if (token !== state.sessionsToken) {
          return;
        }
        state.sessionsError = "Could not reach this local host.";
        showBanner(state.sessionsError);
        renderSessions();
      }
    );
  }

  function applyHostSelection(payload) {
    var fromHost = payload && typeof payload.selectedAppId === "string" ? payload.selectedAppId : "";
    state.selectedAppId = fromHost || null;
  }

  function refreshLastCommand() {
    return request("GET", "/music/last-command").then(
      function (result) {
        if (result.ok) {
          state.lastCommand = result.data;
        }
        renderLastCommand();
      },
      function () {
        renderLastCommand();
      }
    );
  }

  function refreshCs2() {
    if (state.busyCs2) {
      return Promise.resolve();
    }
    return request("GET", "/setup/cs2/status").then(
      function (result) {
        renderCs2(result.ok ? result.data : null, result.ok ? null : describeHostError(result, "/setup/cs2/status"));
      },
      function () {
        renderCs2(null, "Could not read CS2 setup status.");
      }
    );
  }

  function refreshPreset() {
    if (state.busyPreset) {
      return Promise.resolve();
    }
    return request("GET", "/music/preset").then(
      function (result) {
        if (result.ok && result.data && typeof result.data.preset === "string") {
          state.preset = result.data.preset;
        }
        renderPreset();
      },
      function () {
        renderPreset();
      }
    );
  }

  function selectSession(appId) {
    if (!appId || state.busySelect) {
      return;
    }
    state.busySelect = true;
    state.selectNote = null;
    renderSelection();
    request("POST", "/music/session", { appId: appId }).then(
      function (result) {
        state.busySelect = false;
        if (result.ok && result.data && typeof result.data.selectedAppId === "string") {
          state.selectedAppId = result.data.selectedAppId;
          state.selectNote = null;
          refreshSessions();
          return;
        }
        if (result.status === 409) {
          state.selectNote =
            "That session is not present anymore. Press play in the player, then pick it from the list.";
          renderSelection();
          refreshSessions();
          return;
        }
        if (result.status === 400) {
          state.selectNote = "The host needs an app id to select a session.";
          renderSelection();
          return;
        }
        state.selectNote = describeHostError(result, "/music/session");
        renderSelection();
      },
      function () {
        state.busySelect = false;
        state.selectNote = "Could not reach this local host to select a session.";
        renderSelection();
      }
    );
  }

  function runTest(kind) {
    if (state.busyTest) {
      return;
    }
    if (!state.selectedAppId) {
      setOutcome(els.testOutcome, "warn", "Pick a session first. This page does not choose one for you.");
      return;
    }
    state.busyTest = true;
    els.pause.disabled = true;
    els.resume.disabled = true;
    var path = kind === "pause" ? "/music/test/pause" : "/music/test/resume";
    var actionLabel = kind === "pause" ? "Pause" : "Resume";
    request("POST", path).then(
      function (result) {
        state.busyTest = false;
        els.pause.disabled = false;
        els.resume.disabled = false;
        if (result.ok) {
          setOutcome(els.testOutcome, testKind(result.data), describeTest(actionLabel, result.data));
          refreshLastCommand();
          return;
        }
        setOutcome(els.testOutcome, "warn", describeHostError(result, path));
      },
      function () {
        state.busyTest = false;
        els.pause.disabled = false;
        els.resume.disabled = false;
        setOutcome(els.testOutcome, "warn", "Could not reach this local host to run the test.");
      }
    );
  }

  function installCs2() {
    if (state.busyCs2) {
      return;
    }
    state.busyCs2 = true;
    els.cs2Install.disabled = true;
    request("POST", "/setup/cs2/install").then(
      function (result) {
        state.busyCs2 = false;
        els.cs2Install.disabled = false;
        var data = result.data || {};
        if (result.ok && data.success) {
          var text = data.wasUpdated
            ? "The CS2 config was written and now matches this host."
            : "The CS2 config is in place.";
          setOutcome(els.cs2Outcome, "ok", text);
        } else {
          var reason = readableText(data.error) || describeHostError(result, "/setup/cs2/install");
          setOutcome(els.cs2Outcome, "warn", reason);
        }
        refreshCs2();
      },
      function () {
        state.busyCs2 = false;
        els.cs2Install.disabled = false;
        setOutcome(els.cs2Outcome, "warn", "Could not reach this local host to install the CS2 config.");
      }
    );
  }

  function setPreset(name) {
    if (state.busyPreset) {
      return;
    }
    state.busyPreset = true;
    request("POST", "/music/preset", { preset: name }).then(
      function (result) {
        state.busyPreset = false;
        if (result.ok && result.data && typeof result.data.preset === "string") {
          state.preset = result.data.preset;
          els.presetStatus.textContent = presetSummary(state.preset);
          renderPreset();
          return;
        }
        if (result.status === 400) {
          els.presetStatus.textContent = "The host did not accept that behavior name.";
          return;
        }
        els.presetStatus.textContent = describeHostError(result, "/music/preset");
      },
      function () {
        state.busyPreset = false;
        els.presetStatus.textContent = "Could not reach this local host to change round behavior.";
      }
    );
  }

  function renderSessions() {
    var payload = state.sessionsPayload;
    var sessions = payload && Array.isArray(payload.sessions) ? payload.sessions : [];
    var fingerprint = JSON.stringify({
      error: state.sessionsError,
      selectedAppId: payload ? payload.selectedAppId : null,
      sessions: sessions
    });
    if (fingerprint === state.sessionsFingerprint) {
      return;
    }
    state.sessionsFingerprint = fingerprint;

    var focusedAppId = null;
    var active = document.activeElement;
    if (active && els.sessionsList.contains(active)) {
      focusedAppId = active.getAttribute("data-app-id");
    }

    els.sessionsList.replaceChildren();

    if (state.sessionsError && sessions.length === 0) {
      els.sessionsList.appendChild(emptyNote(state.sessionsError));
      stampUpdated();
      return;
    }

    if (sessions.length === 0) {
      els.sessionsList.appendChild(emptyNote(EMPTY_COPY));
      stampUpdated();
      return;
    }

    sessions.forEach(function (session) {
      els.sessionsList.appendChild(sessionButton(session, state.selectedAppId));
    });

    if (focusedAppId) {
      var again = els.sessionsList.querySelector('[data-app-id="' + cssEscape(focusedAppId) + '"]');
      if (again) {
        again.focus();
      }
    }
    stampUpdated();
  }

  function sessionButton(session, selectedAppId) {
    var appId = typeof session.appId === "string" ? session.appId : "";
    var button = document.createElement("button");
    button.type = "button";
    button.className = "session";
    button.setAttribute("data-app-id", appId);
    var isSelected = session.isSelected === true || (appId && appId === selectedAppId);
    button.setAttribute("aria-pressed", isSelected ? "true" : "false");

    var name = document.createElement("span");
    name.className = "session-name";
    name.textContent = displayLabel(session);
    button.appendChild(name);

    if (shouldShowRawId(session)) {
      var raw = document.createElement("span");
      raw.className = "session-id";
      raw.textContent = appId;
      button.appendChild(raw);
    }

    var meta = document.createElement("span");
    meta.className = "session-meta";
    meta.textContent = playbackLine(session);
    button.appendChild(meta);

    if (session.isWindowsCurrent === true) {
      var hint = document.createElement("span");
      hint.className = "hint";
      hint.textContent = "Windows considers this current";
      button.appendChild(hint);
    }

    return button;
  }

  function displayLabel(session) {
    var appId = typeof session.appId === "string" ? session.appId : "";
    var name = typeof session.displayName === "string" ? session.displayName.trim() : "";
    if (!name || name === appId) {
      return appId || "Unknown app id";
    }
    return name;
  }

  function shouldShowRawId(session) {
    var appId = typeof session.appId === "string" ? session.appId : "";
    var name = typeof session.displayName === "string" ? session.displayName.trim() : "";
    return Boolean(appId && name && name !== appId);
  }

  function playbackLine(session) {
    var statusKey = typeof session.playbackStatus === "string" ? session.playbackStatus : "Unknown";
    var status = PLAYBACK_LABELS[statusKey] || statusKey;
    var track = session.track || {};
    var title = readableText(track.title);
    var artist = readableText(track.artist);
    var detail = "No track info";
    if (title && artist) {
      detail = title + " — " + artist;
    } else if (title) {
      detail = title;
    } else if (artist) {
      detail = artist;
    }
    return status + " · " + detail;
  }

  function renderSelection() {
    if (state.busySelect) {
      els.selectionStatus.textContent = "Selecting…";
      return;
    }
    if (state.selectNote) {
      els.selectionStatus.textContent = state.selectNote;
      return;
    }
    var selected = findSelectedSession();
    if (selected) {
      els.selectionStatus.textContent =
        "This host is set to control " + displayLabel(selected) + ". Click a session to change it.";
      return;
    }
    if (state.selectedAppId) {
      els.selectionStatus.textContent =
        "This host still has a selection (" +
        state.selectedAppId +
        "), but that session is not in the list right now. Press play in the player, then pick it again.";
      return;
    }
    els.selectionStatus.textContent = "Nothing selected. Click a session to choose it.";
  }

  function findSelectedSession() {
    var payload = state.sessionsPayload;
    var sessions = payload && Array.isArray(payload.sessions) ? payload.sessions : [];
    for (var i = 0; i < sessions.length; i += 1) {
      var session = sessions[i];
      if (session.isSelected === true || (state.selectedAppId && session.appId === state.selectedAppId)) {
        return session;
      }
    }
    return null;
  }

  function renderCs2(status, error) {
    if (error && !status) {
      els.cs2Status.textContent = error;
      return;
    }
    if (!status) {
      els.cs2Status.textContent = "CS2 setup status is not available yet.";
      return;
    }
    if (status.isReady) {
      els.cs2Status.textContent = "CS2 Game State Integration is installed and current.";
      return;
    }
    if (status.isCs2Found === false) {
      els.cs2Status.textContent =
        readableText(status.error) ||
        "This machine does not have a CS2 install the host can see.";
      return;
    }
    if (status.isCfgInstalled && status.isCfgCurrent === false) {
      els.cs2Status.textContent =
        "The CS2 config is present but does not match this host. Install again to update it.";
      return;
    }
    els.cs2Status.textContent = "CS2 is present. The Game State Integration config is not installed yet.";
  }

  function renderPreset() {
    var current = state.preset;
    els.flow.setAttribute("aria-pressed", current === "Flow" ? "true" : "false");
    els.focus.setAttribute("aria-pressed", current === "Focus" ? "true" : "false");
    if (current) {
      els.presetStatus.textContent = presetSummary(current);
    } else {
      els.presetStatus.textContent = "Current round behavior is not available yet.";
    }
  }

  function presetSummary(name) {
    if (name === "Flow") {
      return "Current: Flow. Music during the round.";
    }
    if (name === "Focus") {
      return "Current: Focus. Quiet while you are alive.";
    }
    return "Current: " + name + ".";
  }

  function renderLastCommand() {
    var data = state.lastCommand;
    if (!data || (data.command == null && data.atUtc == null && data.outcome == null)) {
      els.lastCommand.textContent = "No command has been sent yet.";
      return;
    }
    var commandKey = typeof data.command === "string" ? data.command : "";
    var command = COMMAND_LABELS[commandKey] || commandKey || "a command";
    var source =
      data.source === "test"
        ? "a test on this page"
        : data.source === "game"
          ? "a game event"
          : "an unknown source";
    var when = formatWhen(data.atUtc);
    var reason = readableText(data.reason);
    var result = "";
    if (data.outcome === "Applied") {
      result = "It was applied.";
    } else if (reason) {
      result = reason;
    } else if (data.outcome) {
      result = "It did not apply.";
    }
    var target = readableText(data.targetAppId);
    var text = "Last command: " + command + ", from " + source;
    if (when) {
      text += ", at " + when;
    }
    text += ".";
    if (result) {
      text += " " + result;
    }
    if (target) {
      text += " Target: " + target + ".";
    }
    els.lastCommand.textContent = text;
  }

  function describeTest(actionLabel, payload) {
    var data = payload || {};
    if (data.outcome === "Applied") {
      return actionLabel + " reached the player.";
    }
    var reason = readableText(data.reason);
    if (reason) {
      return reason;
    }
    return actionLabel + " did not reach the player.";
  }

  function testKind(payload) {
    return payload && payload.outcome === "Applied" ? "ok" : "warn";
  }

  function describeHostError(result, path) {
    if (result.status === 404) {
      return "This host does not answer " + path + " yet. The page will keep trying.";
    }
    var data = result.data;
    if (data && typeof data === "object") {
      var fromBody = readableText(data.reason) || readableText(data.error) || readableText(data.title);
      if (fromBody && !looksLikeStack(fromBody)) {
        return fromBody;
      }
    }
    if (result.status) {
      return "The host responded with HTTP " + result.status + " for " + path + ".";
    }
    return "The host did not complete " + path + ".";
  }

  function looksLikeStack(text) {
    return / at .+(\.cs:| in )/.test(text) || text.indexOf("\n   at ") !== -1;
  }

  function request(method, path, body) {
    var init = { method: method, headers: {} };
    if (body !== undefined) {
      init.headers["Content-Type"] = "application/json";
      init.body = JSON.stringify(body);
    }
    return fetch(path, init).then(function (response) {
      return response.text().then(function (text) {
        var data = null;
        if (text) {
          try {
            data = JSON.parse(text);
          } catch (err) {
            data = null;
          }
        }
        return {
          ok: response.ok,
          status: response.status,
          data: data
        };
      });
    });
  }

  function emptyNote(text) {
    var p = document.createElement("p");
    p.className = "empty";
    p.textContent = text;
    return p;
  }

  function stampUpdated() {
    els.sessionsUpdated.textContent = "List updated " + formatWhen(new Date().toISOString()) + ". It refreshes on its own.";
  }

  function setOutcome(node, kind, text) {
    node.className = "outcome " + kind;
    node.textContent = text;
  }

  function showBanner(text) {
    els.banner.hidden = false;
    els.banner.textContent = text;
  }

  function hideBanner() {
    els.banner.hidden = true;
    els.banner.textContent = "";
  }

  function readableText(value) {
    if (typeof value !== "string") {
      return "";
    }
    var trimmed = value.trim();
    return trimmed;
  }

  function formatWhen(iso) {
    if (!iso) {
      return "";
    }
    var date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return "";
    }
    return date.toLocaleString();
  }

  function cssEscape(value) {
    if (window.CSS && typeof window.CSS.escape === "function") {
      return window.CSS.escape(value);
    }
    return String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"');
  }
})();
