(function () {
  'use strict';

  // Mana symbol to CSS class mapping
  function formatManaSymbols(text) {
    if (!text) return '';
    return text.replace(/\{([^{}]+)\}/g, function (match, sym) {
      var clean = sym.toLowerCase().replace('/', '');
      var className = 'ms ms-' + clean + ' ms-cost';
      if (clean === 't') className = 'ms ms-tap ms-cost';
      else if (clean === 'q') className = 'ms ms-untap ms-cost';
      return '<i class="' + className + '"></i>';
    });
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  var LOWERCASE_WORDS = new Set(['a', 'an', 'and', 'as', 'at', 'but', 'by', 'for', 'from', 'in', 'into', 'nor', 'of', 'on', 'onto', 'or', 'so', 'the', 'to', 'up', 'with', 'yet']);

  function toTitleCase(str) {
    if (!str) return '';
    return str.split(/(\s+|[-/])/).map(function (segment, index, arr) {
      if (!segment || /^\s+$/.test(segment) || segment === '-' || segment === '/') return segment;
      var lower = segment.toLowerCase();
      var isFirst = (index === 0 || arr[index - 1] === '-' || arr[index - 1] === '/');
      if (!isFirst && LOWERCASE_WORDS.has(lower)) {
        return lower;
      }
      return lower.charAt(0).toUpperCase() + lower.slice(1);
    }).join('');
  }

  function renderFace(face, faceLabel) {
    var html = '<div class="card-face">';
    if (faceLabel) {
      html += '<div class="face-tag">' + escapeHtml(faceLabel) + '</div>';
    }

    // Header
    html += '<div class="header-row">';
    html += '  <div class="name-col">';
    html += '    <div class="card-name-zh">' + escapeHtml(face.n || face.nameZh || '') + '</div>';
    var enName = face.en || face.nameEn;
    if (enName) {
      html += '    <div class="card-name-en">' + escapeHtml(toTitleCase(enName)) + '</div>';
    }
    html += '  </div>';

    if (face.c || face.manaCost) {
      html += '  <div class="cost-row">' + formatManaSymbols(face.c || face.manaCost) + '</div>';
    }
    html += '</div>';

    // Type
    if (face.y || face.type) {
      html += '<div class="type-line">' + escapeHtml(face.y || face.type) + '</div>';
    }

    // Rules
    var rules = face.t || face.oracleText || '';
    if (rules) {
      html += '<div class="rules-text">';
      var lines = rules.split(/\r?\n/);
      for (var i = 0; i < lines.length; i++) {
        var line = lines[i].trim();
        if (line) {
          html += '<div class="rules-line">' + formatManaSymbols(escapeHtml(line)) + '</div>';
        } else {
          html += '<div class="rules-line empty-line"></div>';
        }
      }
      html += '</div>';
    }

    // Flavor
    var flavor = face.f || face.flavorText || '';
    if (flavor) {
      html += '<div class="flavor-text">' + escapeHtml(flavor).replace(/\r?\n/g, '<br>') + '</div>';
    }

    // Bottom row (P/T or Loyalty)
    var pt = (face.p != null && face.q != null) ? (face.p + '/' + face.q) : (face.pt || null);
    var loyalty = face.l || face.loyalty || null;
    var defense = face.d || face.defense || null;

    if (pt || loyalty || defense || face._src) {
      html += '<div class="bottom-row">';
      html += '  <div class="source-tag">' + escapeHtml(face._src || 'MTG-CN') + '</div>';
      if (pt) {
        html += '  <div class="pt-badge">' + escapeHtml(pt) + '</div>';
      } else if (loyalty) {
        html += '  <div class="loyalty-badge">Loyalty: ' + escapeHtml(loyalty) + '</div>';
      } else if (defense) {
        html += '  <div class="loyalty-badge">Defense: ' + escapeHtml(defense) + '</div>';
      }
      html += '</div>';
    }

    html += '</div>';
    return html;
  }

  function displayCard(data) {
    var container = document.getElementById('card-container');
    if (!container) return;

    if (!data) {
      container.innerHTML = '';
      return;
    }

    var html = '<div class="card-card">';
    if (data.faces && Array.isArray(data.faces) && data.faces.length > 1) {
      for (var i = 0; i < data.faces.length; i++) {
        if (i > 0) html += '<div class="face-separator"></div>';
        html += renderFace(data.faces[i], 'Face ' + (i + 1));
      }
    } else {
      html += renderFace(data);
    }
    html += '</div>';

    container.innerHTML = html;
  }

  function updateSettings(settings) {
    if (!settings) return;
    var root = document.documentElement;
    if (settings.bgOpacity != null) root.style.setProperty('--mtgo-bg-opacity', settings.bgOpacity);
    if (settings.nameColor) root.style.setProperty('--mtgo-name-color', settings.nameColor);
    if (settings.nameSize) root.style.setProperty('--mtgo-name-size', settings.nameSize + 'px');
    if (settings.textColor) root.style.setProperty('--mtgo-text-color', settings.textColor);
    if (settings.textSize) root.style.setProperty('--mtgo-text-size', settings.textSize + 'px');
    if (settings.ptColor) root.style.setProperty('--mtgo-pt-color', settings.ptColor);
    if (settings.ptSize) root.style.setProperty('--mtgo-pt-size', settings.ptSize + 'px');
  }

  window.displayCard = displayCard;
  window.updateSettings = updateSettings;

  // Interop with WebView2 host (C# WPF)
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function (event) {
      var msg = event.data;
      if (typeof msg === 'string') {
        try { msg = JSON.parse(msg); } catch (_) {}
      }
      if (msg.action === 'showCard') {
        displayCard(msg.card);
      } else if (msg.action === 'clear') {
        displayCard(null);
      } else if (msg.action === 'settings') {
        updateSettings(msg.settings);
      }
    });

    // Notify host we are ready
    window.chrome.webview.postMessage({ status: 'ready' });
  }
})();
