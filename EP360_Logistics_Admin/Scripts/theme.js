// Modo oscuro manual: alterna data-theme en <html> y lo guarda en localStorage("portalTema").
// El anti-parpadeo (aplicar el tema guardado antes de pintar) va inline en el <head>.
(function () {
    "use strict";

    function temaActual() {
        return document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
    }

    function aplicarTema(tema) {
        document.documentElement.setAttribute("data-theme", tema);
        try { localStorage.setItem("portalTema", tema); } catch (e) { }
    }

    document.addEventListener("DOMContentLoaded", function () {
        var boton = document.getElementById("boton-modo-oscuro");
        if (!boton) return;
        boton.addEventListener("click", function () {
            aplicarTema(temaActual() === "dark" ? "light" : "dark");
        });
    });
})();
