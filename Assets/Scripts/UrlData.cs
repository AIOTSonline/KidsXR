using Firebase.Firestore;

[FirestoreData]
public class UrlData
{
    [FirestoreProperty("URL")]
    public string URL { get; set; }

    [FirestoreProperty("version")]
    public string version { get; set; }
}