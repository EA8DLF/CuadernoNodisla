// Las pruebas de este proyecto no se reparten entre varios hilos.
//
// Casi todas gastan procesador a base de bien: decodificar una ventana son cientos de
// milisegundos de cuentas, y el banco de medida decodifica ciento cincuenta. Repartirlas entre
// hilos no las acaba antes —la maquina ya esta saturada con los otros diez proyectos de pruebas
// que corren a la vez— y en cambio estropea las dos medidas que este proyecto publica: el
// presupuesto por ventana y la columna de coste del banco. Las dos se miden en tiempo de
// procesador del proceso entero, asi que si dos clases de pruebas trabajan a la vez, la que
// mide se apunta tambien el trabajo de la otra.
//
// Dicho de otro modo: el paralelismo aqui no daba velocidad y si daba cifras falsas.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
