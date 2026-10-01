(function () {
  var caja = document.getElementById('buscar'), lista = document.getElementById('capitulos'), res = document.getElementById('resultado');
  var normal = function (t) { return t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(); };
  var datos = (window.INDICE_AYUDA || []).map(function (c) { return { pagina: c.pagina, buscable: normal(c.titulo + ' ' + c.texto) }; });
  function filtrar() {
    var palabras = normal(caja.value).split(/\s+/).filter(Boolean), n = 0;
    Array.prototype.forEach.call(lista.querySelectorAll('li'), function (li) {
      var href = li.querySelector('a').getAttribute('href');
      var c = datos.find(function (d) { return d.pagina === href; });
      var ok = !palabras.length || (c && palabras.every(function (p) { return c.buscable.indexOf(p) >= 0; }));
      li.style.display = ok ? '' : 'none'; if (ok) n++;
    });
    res.textContent = !palabras.length ? '' : n === 0 ? 'Ningún capítulo lo menciona.' : n === 1 ? 'Lo menciona 1 capítulo.' : 'Lo mencionan ' + n + ' capítulos.';
    try { sessionStorage.setItem('buscarAyuda', caja.value); } catch (e) { }
  }
  try { caja.value = sessionStorage.getItem('buscarAyuda') || ''; } catch (e) { }
  caja.addEventListener('input', filtrar); filtrar();
})();