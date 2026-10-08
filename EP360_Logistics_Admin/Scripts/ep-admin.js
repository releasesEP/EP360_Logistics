/* EP360 Logistics - Administracion. Comportamiento compartido de todas las pantallas.
   - Modal de resultado con el sello (TempData Exito/Error), calcado de alertas.js de HelpDesk.
   - Modal de confirmacion para formularios con data-confirmar (desactivar, quitar, etc.).
   - Filtros de listado que se aplican solos al cambiarlos.
   - Buscador dentro de una tabla (data-filtrar-tabla). */
(function () {
    "use strict";

    function $(sel, ctx) { return (ctx || document).querySelector(sel); }
    function $$(sel, ctx) { return Array.prototype.slice.call((ctx || document).querySelectorAll(sel)); }

    var ICONOS = { exito: "fa-check", error: "fa-xmark", aviso: "fa-exclamation", peligro: "fa-triangle-exclamation", normal: "fa-question" };
    var ETIQUETAS = { exito: "Listo", error: "No se pudo", aviso: "Atención", peligro: "Confirma la acción", normal: "Confirma la acción" };

    function prepararSello(modal, tipo) {
        var sello = $(".marca-sello", modal);
        sello.className = "marca-sello marca-sello--" + tipo;
        $(".marca-sello-badge-circulo i", modal).className = "fas " + (ICONOS[tipo] || ICONOS.normal);
        var etiqueta = $(".modal-alerta__etiqueta", modal);
        etiqueta.className = "modal-alerta__etiqueta modal-alerta__etiqueta--" + tipo;
        etiqueta.textContent = ETIQUETAS[tipo] || "";
        // Reinicia la animacion de la insignia cada vez que se abre.
        var badge = $(".marca-sello-badge", modal);
        badge.style.animation = "none"; void badge.offsetWidth; badge.style.animation = "";
    }

    // ---------- Resultado de la ultima accion ----------
    function mostrarResultado(tipo, mensaje, cuerpo) {
        var modal = $("#modalResultadoAccion");
        if (!modal || !window.bootstrap) return;
        prepararSello(modal, tipo);
        $(".modal-alerta__mensaje", modal).textContent = mensaje;
        var c = $(".modal-alerta__cuerpo", modal);
        c.textContent = cuerpo || "";
        c.style.display = cuerpo ? "" : "none";
        bootstrap.Modal.getOrCreateInstance(modal).show();
    }

    // ---------- Confirmacion antes de enviar ----------
    var formularioPendiente = null;

    function pedirConfirmacion(form) {
        var modal = $("#modalConfirmarAccion");
        if (!modal || !window.bootstrap) return true;
        var tipo = form.getAttribute("data-confirmar-tipo") || "peligro";
        prepararSello(modal, tipo);
        $(".modal-alerta__mensaje", modal).textContent = form.getAttribute("data-confirmar");
        var c = $(".modal-alerta__cuerpo", modal);
        var detalle = form.getAttribute("data-confirmar-detalle");
        c.textContent = detalle || "";
        c.style.display = detalle ? "" : "none";
        var boton = $("#botonConfirmarAccion", modal);
        boton.textContent = form.getAttribute("data-confirmar-boton") || "Sí, continuar";
        boton.className = tipo === "peligro" ? "btn-peligro" : "btn-corporativo";
        formularioPendiente = form;
        bootstrap.Modal.getOrCreateInstance(modal).show();
        return false;
    }

    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form.hasAttribute || !form.hasAttribute("data-confirmar") || form.dataset.confirmado === "1") return;
        e.preventDefault();
        pedirConfirmacion(form);
    }, true);

    document.addEventListener("DOMContentLoaded", function () {
        actualizarDependientes(document);

        // ---------- Menu desplegable (se abre con la marca) ----------
        var panel = $("#menuPanel");
        if (panel && window.bootstrap) {
            var marca = $(".topbar .boton-marca");
            panel.addEventListener("show.bs.offcanvas", function () {
                document.body.classList.add("menu-desplegado");
                if (marca) marca.setAttribute("aria-expanded", "true");
            });
            panel.addEventListener("hide.bs.offcanvas", function () {
                document.body.classList.remove("menu-desplegado");
                if (marca) marca.setAttribute("aria-expanded", "false");
            });
            // Tecla M abre/cierra el menu (no cuando se esta escribiendo en un campo).
            document.addEventListener("keydown", function (e) {
                if ((e.key !== "m" && e.key !== "M") || e.ctrlKey || e.altKey || e.metaKey) return;
                var t = e.target;
                if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
                bootstrap.Offcanvas.getOrCreateInstance(panel).toggle();
            });
        }

        // Los menus de acciones de las tablas se posicionan "fixed": asi la tabla puede desplazarse
        // a lo ancho (celular) sin que su contenedor recorte el menu.
        if (window.bootstrap) {
            $$(".tabla-ep [data-bs-toggle='dropdown']").forEach(function (b) {
                bootstrap.Dropdown.getOrCreateInstance(b, {
                    popperConfig: function (config) { config.strategy = "fixed"; return config; }
                });
            });
        }

        var botonConfirmar = $("#botonConfirmarAccion");
        if (botonConfirmar) {
            botonConfirmar.addEventListener("click", function () {
                if (!formularioPendiente) return;
                formularioPendiente.dataset.confirmado = "1";
                botonConfirmar.disabled = true;
                formularioPendiente.submit();
            });
        }

        // Aviso que dejo la accion anterior (TempData) en el layout.
        var aviso = $("#datosAviso");
        if (aviso) mostrarResultado(aviso.getAttribute("data-tipo"), aviso.getAttribute("data-mensaje"), aviso.getAttribute("data-cuerpo"));

        // ---------- Filtros que se aplican solos ----------
        $$("form.filtros-auto").forEach(function (form) {
            // Los campos vacios no viajan en la URL (queda ?estado=activos en vez de ?buscar=&idGrupo=...).
            form.addEventListener("submit", function () {
                $$("input, select", form).forEach(function (c) { if (!c.value) c.disabled = true; });
            });
            $$("select, input[type=checkbox]", form).forEach(function (control) {
                control.addEventListener("change", function () {
                    var interruptor = control.closest(".listado-interruptor");
                    if (interruptor) interruptor.classList.toggle("activo", control.checked);
                    (form.requestSubmit ? form.requestSubmit() : form.submit());
                });
            });
            var buscar = $("input[type=search]", form);
            if (buscar) {
                var temporizador;
                buscar.addEventListener("input", function () {
                    clearTimeout(temporizador);
                    temporizador = setTimeout(function () { form.requestSubmit ? form.requestSubmit() : form.submit(); }, 650);
                });
                // Al volver de una busqueda, el cursor regresa al final del texto.
                if (buscar.value && buscar.hasAttribute("autofocus")) {
                    var largo = buscar.value.length;
                    buscar.focus();
                    try { buscar.setSelectionRange(largo, largo); } catch (ex) { }
                }
            }
        });

        // ---------- Buscador dentro de una tabla ----------
        $$("[data-filtrar-tabla]").forEach(function (input) {
            var tabla = $(input.getAttribute("data-filtrar-tabla"));
            if (!tabla) return;
            var vacio = $(input.getAttribute("data-filtrar-vacio") || "#__sin__");
            input.addEventListener("input", function () {
                var q = input.value.trim().toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
                var visibles = 0;
                $$("tbody tr[data-fila]", tabla).forEach(function (tr) {
                    var texto = tr.textContent.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
                    var ver = !q || texto.indexOf(q) >= 0;
                    tr.style.display = ver ? "" : "none";
                    if (ver) visibles++;
                });
                if (vacio) vacio.style.display = visibles ? "none" : "";
            });
        });

        // ---------- La pestana de un detalle se recuerda en la URL (#pestana) ----------
        var pestanas = $$(".pestanas-ep [data-bs-toggle='pill']");
        if (pestanas.length && window.bootstrap) {
            var hash = location.hash;
            if (hash) {
                var boton = $(".pestanas-ep [data-bs-target='" + hash + "']");
                if (boton) bootstrap.Tab.getOrCreateInstance(boton).show();
            }
            pestanas.forEach(function (b) {
                b.addEventListener("shown.bs.tab", function () { history.replaceState(null, "", b.getAttribute("data-bs-target")); });
            });
        }
    });

    // ---------- Campos que se muestran segun una opcion (data-mostrar-si="NombreCampo=valor|otro") ----------
    // Se evaluan por formulario y en vivo, asi tambien funcionan en el contenido que llega a un modal.
    function actualizarDependientes(contexto) {
        $$("[data-mostrar-si]", contexto).forEach(function (el) {
            var partes = el.getAttribute("data-mostrar-si").split("=");
            var form = el.closest("form") || document;
            var marcado = $("[name='" + partes[0] + "']:checked", form) || $("select[name='" + partes[0] + "']", form);
            el.style.display = marcado && partes[1].split("|").indexOf(marcado.value) >= 0 ? "" : "none";
        });
    }

    document.addEventListener("change", function (e) {
        var t = e.target;
        // data-excluye="OtroCampo": elegir un valor aqui limpia el otro (ej. grupo o cuenta, no ambos).
        if (t.getAttribute && t.getAttribute("data-excluye") && t.value) {
            var otro = $("[name='" + t.getAttribute("data-excluye") + "']", t.closest("form") || document);
            if (otro) otro.value = "";
        }
        var form = t.closest ? t.closest("form") : null;
        if (form) actualizarDependientes(form);
    });

    // ---------- Formularios cortos en modal (enlaces con data-modal) ----------
    // El enlace sigue apuntando a la pagina del formulario: sin JS, o si algo falla, se navega normal.
    var CABECERA_AJAX = { "X-Requested-With": "XMLHttpRequest" };

    function prepararModal(dialogo) {
        actualizarDependientes(dialogo);
        var campo = $("[autofocus]", dialogo) || $("input:not([type=hidden]), select", dialogo);
        if (campo) setTimeout(function () { campo.focus(); }, 200);
    }

    document.addEventListener("click", function (e) {
        var enlace = e.target.closest ? e.target.closest("a[data-modal]") : null;
        if (!enlace || !window.bootstrap || e.ctrlKey || e.metaKey || e.shiftKey) return;
        e.preventDefault();
        var modal = $("#modalFormulario"), dialogo = $(".modal-dialog", modal);
        // data-modal="lg" para los formularios con mas campos (personas, cuentas, cajas).
        dialogo.style.maxWidth = enlace.getAttribute("data-modal") === "lg" ? "860px" : "640px";
        fetch(enlace.href, { headers: CABECERA_AJAX, credentials: "same-origin" })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(function (html) {
                dialogo.innerHTML = html;
                bootstrap.Modal.getOrCreateInstance(modal).show();
                prepararModal(dialogo);
            })
            .catch(function () { location.href = enlace.href; });
    });

    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!form.hasAttribute || !form.hasAttribute("data-form-modal")) return;
        e.preventDefault();
        var boton = $("button[type=submit]", form), textoBoton = boton ? boton.innerHTML : "";
        if (boton) { boton.disabled = true; boton.innerHTML = '<span class="cargando-ep"></span> Guardando…'; }
        fetch(form.action, { method: "POST", body: new FormData(form), headers: CABECERA_AJAX, credentials: "same-origin" })
            .then(function (r) {
                var tipo = r.headers.get("Content-Type") || "";
                return tipo.indexOf("application/json") >= 0 ? r.json() : r.text();
            })
            .then(function (respuesta) {
                if (respuesta && respuesta.ok) { location.href = respuesta.url; return; }
                // Volvio el formulario con el error (validacion o regla de la BD): se queda abierto con lo capturado.
                var dialogo = form.closest(".modal-dialog");
                dialogo.innerHTML = respuesta;
                prepararModal(dialogo);
            })
            .catch(function () {
                if (boton) { boton.disabled = false; boton.innerHTML = textoBoton; }
                mostrarResultado("error", "No se pudo guardar", "Revisa tu conexión e inténtalo de nuevo.");
            });
    });

    window.EPAdmin = { mostrarResultado: mostrarResultado };
})();
