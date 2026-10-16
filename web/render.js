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

  var currentSettings = {
    showEnRules: true
  };

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

    // Rules (Chinese)
    var rules = face.t || face.rulesText || '';
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

    // English Oracle Rules (if present)
    var enRules = face.oracleText || face.enRules || '';
    if (enRules && typeof enRules === 'string') {
      var displayStyle = (currentSettings.showEnRules === false) ? 'none' : 'block';
      html += '<div class="rules-text-en" style="display: ' + displayStyle + ';">';
      var enLines = enRules.split(/\r?\n/);
      for (var j = 0; j < enLines.length; j++) {
        var enLine = enLines[j].trim();
        if (enLine) {
          html += '<div class="rules-line-en">' + formatManaSymbols(escapeHtml(enLine)) + '</div>';
        } else {
          html += '<div class="rules-line-en empty-line"></div>';
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

  function hexToRgba(hex, opacity) {
    if (!hex) hex = '#1a1c21';
    hex = hex.replace('#', '');
    if (hex.length === 3) hex = hex[0]+hex[0] + hex[1]+hex[1] + hex[2]+hex[2];
    var r = parseInt(hex.substring(0, 2), 16) || 26;
    var g = parseInt(hex.substring(2, 4), 16) || 28;
    var b = parseInt(hex.substring(4, 6), 16) || 33;
    var op = (opacity != null) ? opacity : 0.85;
    return 'rgba(' + r + ', ' + g + ', ' + b + ', ' + op + ')';
  }

  function updateSettings(settings) {
    if (!settings) return;
    var root = document.documentElement;

    if (settings.bgColor || settings.bgOpacity != null) {
      root.style.setProperty('--mtgo-card-bg', hexToRgba(settings.bgColor, settings.bgOpacity));
    }
    if (settings.nameZhColor) root.style.setProperty('--mtgo-name-zh-color', settings.nameZhColor);
    if (settings.nameZhSize) root.style.setProperty('--mtgo-name-zh-size', settings.nameZhSize + 'px');
    if (settings.nameEnColor) root.style.setProperty('--mtgo-name-en-color', settings.nameEnColor);
    if (settings.nameEnSize) root.style.setProperty('--mtgo-name-en-size', settings.nameEnSize + 'px');

    if (settings.typeColor) root.style.setProperty('--mtgo-type-color', settings.typeColor);
    if (settings.typeSize) root.style.setProperty('--mtgo-type-size', settings.typeSize + 'px');

    if (settings.rulesColor) root.style.setProperty('--mtgo-rules-color', settings.rulesColor);
    if (settings.rulesSize) root.style.setProperty('--mtgo-rules-size', settings.rulesSize + 'px');

    if (settings.enRulesColor) {
      root.style.setProperty('--mtgo-en-rules-color', settings.enRulesColor);
      var enNodes = document.querySelectorAll('.rules-text-en');
      for (var k = 0; k < enNodes.length; k++) {
        enNodes[k].style.color = settings.enRulesColor;
      }
    }
    if (settings.enRulesSize) {
      root.style.setProperty('--mtgo-en-rules-size', settings.enRulesSize + 'px');
      var enNodes = document.querySelectorAll('.rules-text-en');
      for (var k = 0; k < enNodes.length; k++) {
        enNodes[k].style.fontSize = settings.enRulesSize + 'px';
      }
    }
    if (settings.showEnRules != null) {
      currentSettings.showEnRules = !!settings.showEnRules;
      root.style.setProperty('--mtgo-en-rules-display', currentSettings.showEnRules ? 'block' : 'none');
      var enNodes = document.querySelectorAll('.rules-text-en');
      for (var k = 0; k < enNodes.length; k++) {
        enNodes[k].style.display = currentSettings.showEnRules ? 'block' : 'none';
      }
    }

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
