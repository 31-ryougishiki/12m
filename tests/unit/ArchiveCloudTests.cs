using System;
using System.IO;
using System.Text;
namespace ElymsPet {
 static class ArchiveCloudTests {
  static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS cloud: "+name);}
  static void Reject(Action action,string name){try{action();}catch{Console.WriteLine("PASS cloud: "+name);return;}throw new Exception(name);}
  public static void Run(string root){
   string original=Storage.Folder;Storage.Folder=Path.Combine(root,"cloud");
   try {
    var book=ArchiveStore.Normalize(new ArchiveBook());book.Slots[0].Name="云端测试";
    var bytes=ArchiveCloudCodec.Encode(book);var restored=ArchiveCloudCodec.Decode(bytes);
    Check(restored.Slots[0].Name=="云端测试"&&restored.ActiveId==book.ActiveId,"archive round trip preserves slot identity");
    Check(ArchiveCloudCodec.Hash(bytes)==ArchiveCloudCodec.Hash(ArchiveCloudCodec.Encode(restored)),"normalized content hash stable");
    Reject(delegate{ArchiveCloudCodec.Decode(Encoding.UTF8.GetBytes("{}"));},"empty response rejected");
    book.Version=99;Reject(delegate{ArchiveCloudCodec.Decode(ArchiveCloudCodec.Encode(book));},"future schema rejected");book.Version=4;
    book.Slots[0].Id="../../outside";Reject(delegate{ArchiveCloudCodec.Decode(ArchiveCloudCodec.Encode(book));},"path traversal slot rejected");
    Reject(delegate{ArchiveCloudCodec.Decode(new byte[ArchiveCloudCodec.Limit+1]);},"oversized response rejected");
    var config=new ArchiveCloudConfig{Bucket="test-bucket",Region="cn-shenzhen"};config.SetCredentials("test-id","test-secret");config.Save();
    Check(!File.ReadAllText(ArchiveCloudConfig.PathName).Contains("test-secret"),"credentials encrypted at rest");
    Check(ArchiveCloudConfig.Load().Credentials()[1]=="test-secret","encrypted credentials round trip");
    config.Region="cn-shenzhen.attacker.test";Reject(config.Validate,"arbitrary endpoint rejected");config.Region="cn-shenzhen";
    config.Prefix="../";Reject(config.Validate,"invalid prefix rejected");config.Prefix="elyms/archives/";
    Reject(delegate{new ArchiveOssClient(config).Download("other/private.json");},"download outside configured prefix rejected before network");
    // Published Alibaba Cloud V4 signing example, including additional signed headers.
    string canonical="PUT\n/examplebucket/exampleobject\n\ncontent-disposition:attachment\ncontent-length:3\ncontent-md5:ICy5YqxZB1uWSwcVLSNLcA==\ncontent-type:text/plain\nx-oss-content-sha256:UNSIGNED-PAYLOAD\nx-oss-date:20250411T064124Z\n\ncontent-disposition;content-length\nUNSIGNED-PAYLOAD";
    Check(ArchiveCloudCodec.Hash(Encoding.UTF8.GetBytes(canonical))=="c46d96390bdbc2d739ac9363293ae9d710b14e48081fcb22cd8ad54b63136eca","official canonical request vector");
    Check(ArchiveOssClient.Sign("yourAccessKeySecret","cn-hangzhou","20250411T064124Z",canonical)=="d3694c2dfc5371ee6acd35e88c4871ac95a7ba01d3a2f476768fe61218590097","OSS V4 signature independently checked with Python hmac");
   }finally{Storage.Folder=original;}
  }
 }
}

