// Phase 22C deliberately does not execute this file. It exists to demonstrate
// that the safe static-web adapter ignores project JavaScript.
document.querySelector("button")?.addEventListener("click", () => {
  document.querySelector(".progress").textContent = "Goal celebrated!";
});
