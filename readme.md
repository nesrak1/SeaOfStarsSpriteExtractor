# SOSSE

Extracts sprites from SOS. Just a simple example of AssetsTools.NET and AddressablesTools.NET.

```
dumpcatalog <catalog.json path> - print catalog paths
dumpspritecatalog <catalog.json path> <sprite path> <output.png path> - dump from catalog path
```

Run dumpcatalog to figure out the paths. Then use dumpspritecatalog to dump the sprite. Make sure that you provide a path that ends with `_atlas-i.png` or else SOSSE won't be able to find the palette.