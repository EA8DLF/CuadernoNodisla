using Xunit;

// Las pruebas de este modulo miden tiempos: cuanto tarda el vigilante en soltar el PTT, cuanto
// tarda el control en darse cuenta de que el equipo no esta. Si corrieran en paralelo entre
// ellas, unas le robarian el reloj a las otras y acabariamos con pruebas que fallan a veces,
// que es lo peor que le puede pasar a un modulo como este: enseñan a ignorar los fallos.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
