window.ems = {
  load: function (k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
  save: function (k, v) { try { localStorage.setItem(k, v); return true; } catch (e) { return false; } },
  sget: function (k) { try { return sessionStorage.getItem(k); } catch (e) { return null; } },
  sset: function (k, v) { try { sessionStorage.setItem(k, v); } catch (e) { } },
  sremove: function (k) { try { sessionStorage.removeItem(k); } catch (e) { } },

  getTheme: function () { return document.documentElement.getAttribute("data-theme") || "light"; },
  toggleTheme: function () {
    var next = window.ems.getTheme() === "dark" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", next);
    try { localStorage.setItem("ems.theme", next); } catch (e) { }
    return next;
  },

  now: function () {
    var d = new Date();
    function p(n) { return (n < 10 ? "0" : "") + n; }
    return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) + "T" +
           p(d.getHours()) + ":" + p(d.getMinutes()) + ":" + p(d.getSeconds());
  },

  startCamera: async function (id) {
    try {
      var v = document.getElementById(id);
      if (!v || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return false;
      var s = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user" }, audio: false });
      v.srcObject = s;
      await v.play();
      return true;
    } catch (e) { return false; }
  },

  stopCamera: function (id) {
    var v = document.getElementById(id);
    if (v && v.srcObject) {
      v.srcObject.getTracks().forEach(function (t) { t.stop(); });
      v.srcObject = null;
    }
  },

  _draw: function (src, w, h, maxW) {
    var scale = Math.min(1, maxW / w);
    var c = document.createElement("canvas");
    c.width = Math.max(1, Math.round(w * scale));
    c.height = Math.max(1, Math.round(h * scale));
    c.getContext("2d").drawImage(src, 0, 0, c.width, c.height);
    return c.toDataURL("image/jpeg", 0.6);
  },

  capture: function (id, maxW) {
    var v = document.getElementById(id);
    if (!v || !v.videoWidth) return "";
    return window.ems._draw(v, v.videoWidth, v.videoHeight, maxW);
  },

  fileToDataUrl: function (inputId, maxW) {
    return new Promise(function (resolve) {
      var input = document.getElementById(inputId);
      if (!input || !input.files || !input.files[0]) { resolve(""); return; }
      var reader = new FileReader();
      reader.onerror = function () { resolve(""); };
      reader.onload = function () {
        var img = new Image();
        img.onerror = function () { resolve(""); };
        img.onload = function () { resolve(window.ems._draw(img, img.width, img.height, maxW)); };
        img.src = reader.result;
      };
      reader.readAsDataURL(input.files[0]);
    });
  }
};
