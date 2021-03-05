using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;


namespace PEHelloWorld
{
    class PEHelloWorld
    {
        static void Main(string[] args)
        {
            if (args.Length != 2) {
                Console.Error.WriteLine ("usage: program input.json output.pefile");
            }

            var inputFilePath = args[0];
            var outputFilePath = args[1];

            var self = new PEHelloWorld ();


            Dictionary<string,string> configProperties = self.ConvertInputToDictionary (inputFilePath);

#if USE_METADATA
    	        var metadataBuilder = self.CreateMinimalMetadata("runtimeconfig.json");
                self.ConvertDictionaryToMetadata (configProperties, metadataBuilder);

                using var stream = File.OpenWrite(outputFilePath);
                self.WriteContentAsPEImageToStream (metadataBuilder, stream);
#else
                var blobBuilder = new BlobBuilder();
                self.ConvertDictionaryToBlob (configProperties, blobBuilder);
                using var stream = File.OpenWrite(outputFilePath);
                blobBuilder.WriteContentTo(stream);
#endif
        }

        /// Reads a json file from the given path and extracts the "configProperties" key (assumed to be a string to string dictionary)
        private Dictionary<string, string> ConvertInputToDictionary(string inputFilePath)
        {
            var options = new JsonSerializerOptions {
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            };
            using var stream = File.OpenRead(inputFilePath);
            var parsedJson = JsonSerializer.DeserializeAsync<RuntimeConfigModel.Root>(stream, options).Result;

            return parsedJson.ConfigProperties;
        }


        /// Just write the dictionary out to a blob as a count followed by
        /// a length-prefixed UTF8 encoding of each key and value
        private void ConvertDictionaryToBlob (IReadOnlyDictionary<string,string> properties, BlobBuilder b)
        {
            int count = properties.Count;

            b.WriteCompressedInteger (count);
            foreach (var kvp in properties)
            {
                b.WriteSerializedString (kvp.Key);
                b.WriteSerializedString (kvp.Value);
            }
        }

#region An alternative encoding the data as a barebones ECMA335 metadata file
        private void ConvertDictionaryToMetadata (IReadOnlyDictionary<string,string> properties, MetadataBuilder builder)
        {
            int count = properties.Count;
            
            StringHandle[] keys = new StringHandle[count];
            StringHandle[] values = new StringHandle[count];
            int i = 0;
            // Add one module reference for each key and value in order
            foreach (var kvp in properties)
            {
                keys[i] = builder.GetOrAddString(kvp.Key);
                builder.AddModuleReference(keys[i]);
                values[i] = builder.GetOrAddString(kvp.Value);
                builder.AddModuleReference(values[i]);
                i++;
            }
            
        }
       /// Creates a MetadataBuilder that has a Module with the given name and empty GUIDs
        private MetadataBuilder CreateMinimalMetadata (string moduleName)
        {
            var metadataBuilder = new MetadataBuilder ();
            GuidHandle emptyGuidHandle = metadataBuilder.GetOrAddGuid (Guid.Empty);
            metadataBuilder.AddModule(0, metadataBuilder.GetOrAddString(""), mvid: emptyGuidHandle, encId: emptyGuidHandle, encBaseId: emptyGuidHandle);
            return metadataBuilder;
        }

        /// Creates a ManagedPEBuilder using the given metadata as the metadata root
        /// and writes the resulting pe image to the given destination Stream.
        /// The PE image will have no IL.
        private void WriteContentAsPEImageToStream (MetadataBuilder metadataBuilder, Stream destination)
        {
            var ilBuilder = new BlobBuilder ();

            // Inspired by https://csharp.hotexamples.com/examples/System.Reflection.Metadata.Ecma335/MetadataBuilder/-/php-metadatabuilder-class-examples.html
            //var pe = new ManagedPEBuilder (PEHeaderBuilder.CreateLibraryHeader(),
            //            new MetadataRootBuilder (metadataBuilder),
            //            ilBuilder,
            //            flags: CorFlags.ILOnly);

            var b = new BlobBuilder ();

            //pe.Serialize(b);

            var r = new MetadataRootBuilder(metadataBuilder);

            r.Serialize(b, 0, 0);

            b.WriteContentTo(destination);        
        }
#endregion
    }
}

namespace RuntimeConfigModel {
    class Root {
        // the configProperties key
        [JsonPropertyName ("configProperties")]
        public Dictionary<string,string> ConfigProperties {get; set;}
        // everything other than configProperties
        [JsonExtensionData]
        public Dictionary<string,object> ExtensionData {get; set;}
    }
}
