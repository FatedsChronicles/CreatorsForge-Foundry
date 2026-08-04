using System.Reflection.PortableExecutable;
using System.Text;

namespace CreatorsForge.Foundry.Testing;

public static class ObsAbiInspector
{
    public static IReadOnlySet<string> RequiredModuleExports { get; } =
        new HashSet<string>(
            [
                "obs_module_ver",
                "obs_module_set_pointer",
                "obs_module_load",
                "obs_module_name",
                "obs_module_author",
                "obs_module_description",
            ],
            StringComparer.Ordinal);

    public static ObsAbiInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var headers = reader.PEHeaders;
        if (headers.PEHeader is null)
        {
            return new(false, false, false, [], RequiredModuleExports.Order(StringComparer.Ordinal).ToArray());
        }

        var exports = ReadExports(stream, headers);
        return new(
            true,
            headers.CoffHeader.Machine == Machine.Amd64 && headers.PEHeader.Magic == PEMagic.PE32Plus,
            (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0,
            exports,
            RequiredModuleExports.Except(exports, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static string[] ReadExports(Stream stream, PEHeaders headers)
    {
        var directory = headers.PEHeader!.ExportTableDirectory;
        if (directory.RelativeVirtualAddress == 0 || directory.Size < 40)
        {
            return [];
        }

        using var binary = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = RvaToOffset(headers, directory.RelativeVirtualAddress);
        _ = binary.ReadBytes(24);
        var numberOfNames = binary.ReadUInt32();
        _ = binary.ReadUInt32();
        var addressOfNames = binary.ReadUInt32();
        _ = binary.ReadUInt32();
        if (numberOfNames > 4096 || addressOfNames == 0)
        {
            throw new InvalidDataException("The PE export table exceeds Foundry inspection limits.");
        }

        stream.Position = RvaToOffset(headers, checked((int)addressOfNames));
        var nameRvas = new uint[numberOfNames];
        for (var index = 0; index < nameRvas.Length; index++)
        {
            nameRvas[index] = binary.ReadUInt32();
        }

        var names = new List<string>(nameRvas.Length);
        foreach (var rva in nameRvas)
        {
            stream.Position = RvaToOffset(headers, checked((int)rva));
            var bytes = new List<byte>();
            while (bytes.Count < 512)
            {
                var value = binary.ReadByte();
                if (value == 0)
                {
                    break;
                }

                bytes.Add(value);
            }

            if (bytes.Count == 512)
            {
                throw new InvalidDataException("A PE export name exceeds Foundry inspection limits.");
            }

            names.Add(Encoding.ASCII.GetString(bytes.ToArray()));
        }

        return names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static long RvaToOffset(PEHeaders headers, int rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            var size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
            {
                return checked(section.PointerToRawData + (rva - section.VirtualAddress));
            }
        }

        throw new InvalidDataException($"PE RVA 0x{rva:X8} is outside all sections.");
    }
}
