using AddressablesTools;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

if (args.Length == 0)
{
    Console.WriteLine("sosse");
    Console.WriteLine("  dumpcatalog <catalog.json path> - print catalog paths");
    Console.WriteLine("  dumpspritecatalog <catalog.json path> <sprite path> <output.png path> - dump from catalog path");
    return;
}

var RUNTIME_PATH_STRING = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";

var mode = args[0];
if (mode == "dumpcatalog")
{
    DumpCatalog();
}
else if (mode == "dumpspritecatalog")
{
    if (args.Length < 4)
    {
        Console.WriteLine("dumpspritecatalog <catalog.json path> <sprite path> <output.png path>");
        return;
    }

    var catalogPath = args[1];
    var spritePath = args[2];
    var outputPath = args[3];

    if (!spritePath.EndsWith("_atlas-i.png"))
    {
        Console.WriteLine("sprite path must end with _atlas-i.png");
        return;
    }

    var palettePath = spritePath.Replace("_atlas-i.png", "_OriginalColor_palette-i.png");

    var manager = new AssetsManager();

    var ccd = AddressablesJsonParser.FromString(File.ReadAllText(catalogPath));

    if (!ccd.Resources.ContainsKey(spritePath) || !ccd.Resources.ContainsKey(palettePath))
    {
        Console.WriteLine("Couldn't find that sprite or palette in catalog.json");
        return;
    }

    // all of the values should have the same dependency and internalid,
    // so using the first one is fine here.
    var resourceLoc = ccd.Resources[spritePath][0];
    var dependencies = ccd.Resources[resourceLoc.Dependency].Select(d => d.InternalId).ToList();

    var aaPath = Path.GetDirectoryName(catalogPath)!;
    var bundlePaths = dependencies.Select(d => d.Replace(RUNTIME_PATH_STRING, aaPath)).ToList();
    var mainBundle = manager.LoadBundleFile(bundlePaths[0], true);
    // probably not necessary right now but could be in the future
    var bundleDependencies = new List<BundleFileInstance>();
    for (var i = 1; i < bundlePaths.Count; i++)
    {
        bundleDependencies.Add(manager.LoadBundleFile(bundlePaths[i], true));
    }

    var afileInst = GetFirstSerializedFile(manager, mainBundle);
    if (afileInst == null)
    {
        Console.WriteLine("Couldn't find serialized file in bundle");
        return;
    }

    var assetBundleInf = afileInst.file.GetAssetsOfType(AssetClassID.AssetBundle)[0];
    var assetBundleBf = manager.GetBaseField(afileInst, assetBundleInf);
    var containerItems = assetBundleBf["m_Container.Array"];
    AssetPPtr? imagePtr = null;
    AssetPPtr? palettePtr = null;
    foreach (var containerItem in containerItems)
    {
        var first = containerItem["first"].AsString;
        if (first == spritePath)
        {
            var assetType = manager.GetExtAsset(afileInst, containerItem["second.asset"], true).info.TypeId;
            // ignore all the sprites for now, we just want the texture2d
            if (assetType == (int)AssetClassID.Texture2D)
            {
                imagePtr = AssetPPtr.FromField(containerItem["second.asset"]);
            }
        }
        else if (first == palettePath)
        {
            palettePtr = AssetPPtr.FromField(containerItem["second.asset"]);
        }

        if (imagePtr != null && palettePtr != null)
            break;
    }

    if (imagePtr == null || palettePtr == null)
    {
        Console.WriteLine("couldn't find image or palette in asset bundle");
        return;
    }

    var imageTextureExt = manager.GetExtAsset(afileInst, imagePtr.FileId, imagePtr.PathId);
    var paletteTextureExt = manager.GetExtAsset(afileInst, palettePtr.FileId, palettePtr.PathId);

    var imageTextureFile = TextureFile.ReadTextureFile(imageTextureExt.baseField);
    var paletteTextureFile = TextureFile.ReadTextureFile(paletteTextureExt.baseField);

    var imageTexture = ConvertBgraTexture(
        imageTextureFile.GetTextureData(imageTextureExt.file),
        imageTextureFile.m_Width, imageTextureFile.m_Height);

    var paletteTexture = ConvertBgraTexture(
        paletteTextureFile.GetTextureData(paletteTextureExt.file),
        paletteTextureFile.m_Width, paletteTextureFile.m_Height);

    var palette = new Color[256];
    palette[0] = Color.Transparent;
    palette[1] = Color.Black; // todo, what is this color
    for (var x = 2; x < 256; x++)
    {
        palette[x] = paletteTexture[x, 0];
    }

    var newImage = new Image<Bgra32>(imageTextureFile.m_Width, imageTextureFile.m_Height);
    for (var y = 0; y < imageTextureFile.m_Height; y++)
    {
        for (var x = 0; x < imageTextureFile.m_Width; x++)
        {
            var colorIndex = imageTexture[x, y].R;
            newImage[x, y] = palette[colorIndex];
        }
    }

    imageTexture.Save(Path.GetFileName(outputPath) + "_img.png");
    paletteTexture.Save(Path.GetFileName(outputPath) + "_pal.png");
    newImage.Save(outputPath);
}
else
{
    Console.WriteLine($"unknown mode \"{mode}\"");
}

Image<Bgra32> ConvertBgraTexture(byte[] data, int width, int height)
{
    var image = Image.LoadPixelData<Bgra32>(data, width, height);
    image.Mutate(x => x.Flip(FlipMode.Vertical));
    return image;
}

AssetsFileInstance? GetFirstSerializedFile(AssetsManager manager, BundleFileInstance bunInst)
{
    var dirInfos = bunInst.file.BlockAndDirInfo.DirectoryInfos;
    for (var i = 0; i < dirInfos.Count; i++)
    {
        var dir = dirInfos[i];
        if ((dir.Flags & 0x04) != 0)
            return manager.LoadAssetsFileFromBundle(bunInst, i, false);
    }

    return null;
}

void DumpCatalog()
{
    if (args.Length < 2)
    {
        Console.WriteLine("dumpcatalog <catalog.json path>");
        return;
    }

    var catalogPath = args[1];
    var ccd = AddressablesJsonParser.FromString(File.ReadAllText(catalogPath));

    foreach (var kvp in ccd.Resources)
    {
        var key = kvp.Key;
        var value = kvp.Value;

        if (value.Count == 0)
            continue;

        // these will probably be the same except with different types
        // just select any (the first in this case)
        var firstValue = value[0];

        var keyStr = key.ToString();
        if (keyStr == null)
            continue;
        // filter keys with the runtime path at the start
        if (keyStr.StartsWith(RUNTIME_PATH_STRING))
            continue;
        // filter keys that have a different primary key than the lookup key
        if (firstValue.PrimaryKey != keyStr)
            continue;
        // also make sure the internal id doesn't have a runtime path either
        if (firstValue.InternalId.StartsWith(RUNTIME_PATH_STRING))
            continue;
        // we're only looking for bundled assets
        if (firstValue.ProviderId != "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
            continue;

        Console.WriteLine(keyStr);
    }
}

