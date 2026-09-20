(function () {
  const storageKey = "angry-monkey-cloud-commerce-theme";
  function preferredTheme() {
    try {
      const stored = window.localStorage.getItem(storageKey);
      if (stored === "dark" || stored === "light") return stored;
    } catch {}
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  function applyTheme(theme) {
    document.documentElement.dataset.demoTheme = theme;
    try { window.localStorage.setItem(storageKey, theme); } catch {}
  }
  window.cloudCommerceDemo = {
    getTheme: preferredTheme,
    setTheme: applyTheme,
    copyText: async text => {
      if (!navigator.clipboard) throw new Error("Clipboard unavailable");
      await navigator.clipboard.writeText(text);
    },
    download: (name, text) => {
      const url = URL.createObjectURL(new Blob([text], { type: "text/plain;charset=utf-8" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = name;
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
    focus: id => document.getElementById(id)?.focus(),
    scrollToId: id => document.getElementById(id)?.scrollIntoView({ behavior: "smooth", block: "start" })
  };
  document.documentElement.dataset.demoTheme = preferredTheme();
})();
