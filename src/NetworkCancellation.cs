using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
namespace ElymsPet {
 // Every worker owns its registrations; cancellation aborts active HTTP I/O.
 public static class NetworkCancellation {
  [ThreadStatic] static CancellationToken token;
  [ThreadStatic] static List<CancellationTokenRegistration> registrations;
  public static void Track(HttpWebRequest request){token.ThrowIfCancellationRequested();if(registrations!=null)registrations.Add(token.Register(request.Abort));}
  public static T Run<T>(CancellationToken value,Func<T> action){
   var old=token;var previous=registrations;token=value;registrations=new List<CancellationTokenRegistration>();
   try{value.ThrowIfCancellationRequested();return action();}
   finally{foreach(var registration in registrations)registration.Dispose();registrations=previous;token=old;}
  }
 }
}
