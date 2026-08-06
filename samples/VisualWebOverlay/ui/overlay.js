// Structural mode deliberately ignores this file. Phase 22D live mode executes
// it only inside the disposable WebView2 host.
document.documentElement.dataset.foundryRuntime = "javascript-ready";
document.querySelector(".progress").title = "JavaScript executed in isolated preview";
document.querySelector("button")?.addEventListener("click", () => {
  document.querySelector(".progress").textContent = "Goal celebrated!";
});
