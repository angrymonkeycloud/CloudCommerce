(function () {
  const storageKey = "angry-monkey-cloud-commerce-theme";

  function preferredTheme() {
    const stored = window.localStorage.getItem(storageKey);
    if (stored === "dark" || stored === "light")
      return stored;

    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  function applyTheme(theme) {
    document.documentElement.dataset.demoTheme = theme;
    window.localStorage.setItem(storageKey, theme);
  }

  window.cloudCommerceDemo = {
    getTheme: preferredTheme,
    setTheme: applyTheme,
    copyText: async text => navigator.clipboard.writeText(text),
    scrollToId: id => document.getElementById(id)?.scrollIntoView({ behavior: "smooth", block: "start" })
  };

  document.documentElement.dataset.demoTheme = preferredTheme();
})();