using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Text;
using UnityEngine.Networking;
using UnityEngine.Purchasing;
#if UNITY_EDITOR
	using UnityEditor;
#endif

public enum IASAdSize {
	Square = 1,
	Tall = 2
}

public class TextureScale {
	private static Color[] texColors;
	private static Color[] newColors;
	private static int w;
	private static float ratioX;
	private static float ratioY;
	private static int w2;
 
	public static void Scale(Texture2D tex, int newWidth, int newHeight) {
		texColors = tex.GetPixels();
		newColors = new Color[newWidth * newHeight];
		ratioX = 1.0f / ((float)newWidth / (tex.width-1));
		ratioY = 1.0f / ((float)newHeight / (tex.height-1));
		w = tex.width;
		w2 = newWidth;
 
		BilinearScale(0, newHeight);
 
		tex.Reinitialize(newWidth, newHeight);
		tex.SetPixels(newColors);
		tex.Apply();
	}
 
	private static void BilinearScale(int start, int end) {
		for (var y = start; y < end; y++) {
			int yFloor = (int)Mathf.Floor(y * ratioY);
			var y1 = yFloor * w;
			var y2 = (yFloor+1) * w;
			var yw = y * w2;
 
			for (var x = 0; x < w2; x++) {
				int xFloor = (int)Mathf.Floor(x * ratioX);
				var xLerp = x * ratioX-xFloor;
				newColors[yw + x] = ColorLerpUnclamped(ColorLerpUnclamped(texColors[y1 + xFloor], texColors[y1 + xFloor+1], xLerp),
					ColorLerpUnclamped(texColors[y2 + xFloor], texColors[y2 + xFloor+1], xLerp),
					y*ratioY-yFloor);
			}
		}
	}
 
	private static Color ColorLerpUnclamped (Color c1, Color c2, float value) {
		return new Color (c1.r + (c2.r - c1.r) * value, 
			c1.g + (c2.g - c1.g) * value, 
			c1.b + (c2.b - c1.b) * value, 
			c1.a + (c2.a - c1.a) * value);
	}
}

public class IAS_Manager : MonoBehaviour {
	// Storage classes for IAS adverts
	[Serializable]
	public class AdJsonFileData {
		#if UNITY_EDITOR
			public string name;
		#endif

		public List<AdSlotData> slotInts = new List<AdSlotData>();

		public AdJsonFileData(List<AdSlotData> newSlotIntsData = null)
		{
			slotInts = (newSlotIntsData == null ? new List<AdSlotData>() : newSlotIntsData);
		}
	}

	[Serializable]
	public class AdSlotData {
		#if UNITY_EDITOR
			public string name;
		#endif

		public int slotInt; // Number from the slotID

		public List<AdData> advert = new List<AdData>();

		public int lastSlotId;
		public int lastBackscreenId;

		public AdSlotData(int newSlotInt = -1, List<AdData> newAdvert = null)
		{
			slotInt = newSlotInt;
			advert = (newAdvert == null ? new List<AdData>() : newAdvert);
		} 
	}

	[Serializable]
	public class AdData {
		#if UNITY_EDITOR
			public string name;
		#endif

		public int adUniqueId; // The unique ad id from the IAS server (used for tracking impressions/clicks)
		public char slotChar; // Character from the slotID
		public string fileName; // Name this ad file will be named as on the device
		
		public bool hasAnimatedImage; // Has this ad got an animated image?
		public bool isTextureFileCached; // Has the texture been saved to the device
		public bool isTextureReady; // Has the texture finished downloading
		public bool isInstalled; // Is this an advert for an app which is already installed?
		public bool isSelf; // Is this an advert for this game?
		public bool isActive; // Is this an advert marked as active in the json file?
		public bool isDownloading;

		public long lastUpdated; // Timestamp of when the ad was last updated
		public long newUpdateTime; // Timestamp of the newly collected ad data

		public string imgUrl; // URL of the image we need to download
		public string adUrl; // URL the player is taken to when clicking the ad
		public string packageName;

		public int adTextureId = -1; // Reference id to which the texture for this ad is stored in

		public AdData(char inSlotChar = default(char))
		{
			slotChar = inSlotChar;
		}
	}

	// These classes are for the JsonUtility to move the data into after they've been ready from the file
	[Serializable]
	public class JsonFileData {
		public List<JsonSlotData> slots;
	}

	[Serializable]
	public class JsonSlotData {
		public string slotid;
		public int adid;

		public long updatetime;
		public bool active;

		public string adurl;
		public string imgurl;
		public string animated_imgurl;
	}
	
	[Serializable]
	public class IASTextureData {
		[Serializable]
		public class IASAnimatedFrameData {
			public Texture2D texture;
			public float timeUntilFrameChange;

			public IASAnimatedFrameData(Texture2D inTexture, float inTimeUntilFrameChange) {
				texture = inTexture;
				timeUntilFrameChange = inTimeUntilFrameChange;
			}
		}
		
		public Texture2D staticTexture;
		public List<IASAnimatedFrameData> animatedTextureFrames = new List<IASAnimatedFrameData>();
		public int animationFrames = 0;
		
		public IASTextureData(Texture2D inStaticTexture) {
			staticTexture = inStaticTexture;
		}
	}
	
	public static IAS_Manager instance;

	public string bundleId { get; private set; }
	public string appVersion { get; private set; }

	private int internalScriptVersion = 36;

	public string activeJsonURL { get; private set; }
	public int activeSlotId { get; private set; }

	private int slotIdDecimalOffset = 97; // Decimal offset used to start our ASCII character at 'a'

	// List of apps installed on the player device matching our filter
	private List<string> installedApps = new List<string>();

	public bool advancedLogging = false; // Enable this to debug the IAS with more debug logs

	public bool enableAnimatedAdSupport = true; // When enabled animated PNGs will be downloaded and the IAS Handler will convert it into frames at runtime

	public bool compressLoadedTextures = true;
	
	[Tooltip("Most games use 3 square ads on the backscreen (default:3)")]
	public int squareBackscreenAds = 3;

	[Tooltip("Most games don't use tall ads on the backscreen (default:0)")]
	public int tallBackscreenAds = 0;

	public List<IASAdSize> blacklistedSlots = new List<IASAdSize>();

	// Called when DownloadAdData completes (images likely not ready at this callback)
	public static Action OnIASDataReady;
	
	// Called whenever any image finishes downloading
	public static Action<IASAdSize> OnIASImageDownloaded;
	
	// Called when a refresh is done which was flagged as wanting a force change
	public static Action<IASAdSize> OnForceChangeWanted;
	
	// Called when the IAS was force reset (e.g switching animation mode)
	public static Action OnIASForceReset;
	
	// Called whenever any animated texture has finished building all frames of the animation
	public static Action<int> OnAnimatedTexturesReady;

	// Optimization to group together save calls (also delays saving so they won't happen as soon as the app is launched)
	private int framesUntilIASSave = -1;

	// Variable to make sure the force save at app quit isn't called multiple times
	// (is also set back to false if the user comes back to the app from being minimized)
	private bool hasQuitForceSaveBeenCalled = false;

	// Most app stores do not want us linking to other stores so any stores we don't support IAS on just don't show IAS ads
	public bool storeSupportsIAS { get; private set; }
	
	private List<int> loggedImpressions = new List<int>();
	private List<int> loggedClicks = new List<int>();

	private int adTextureInsertId = -1;
	
	// Contains information about the adverts we have available to be displayed and their statuses
	private AdJsonFileData advertData = new AdJsonFileData();

	// The textures are in a separate list so we can serialize the advertData to save it across sessions
	[HideInInspector] public List<IASTextureData> advertTextures = new List<IASTextureData>();
	
	// Standardised header for all PNG files, must be the first 8 bytes of any PNG
    private byte[] pngHeader = new byte[] {137, 80, 78, 71, 13, 10, 26, 10};

    private class ImageMetaData {
        // IHDR
        public int width;
        public int height;
        public byte bitDepth; // 1, 2, 4, 8, 16
        public byte colorType; // 0 grayscale, 2 truecolor, 3 indexed-color, 4 grayscale with alpha, 6 truecolor with alpha
        public byte compressionMethod; // Always 0, PNG does not have other compression methods at this time
        public byte filterMethod; // Always 0, PNG does not have other filter methods at this time
        public byte interlaceMethod; // 0 none, 1 adam7 interlace
        public int[] chromaKeyValues; // transparencyInfoBytes converted to chromaKeyValues (not used in color types 4 and 6)

        // PLTE (only used for color type 2, 3 and 6) (3 it must be used, 2 and 6 are optional)
        public byte[] colorPaletteBytes = new byte[0];

        // tRNS (not used in color types 4 and 6 as transparency is defined per pixel rather than using a chroma key)
        public byte[] transparencyInfoBytes = new byte[0];
    }

    private class FrameData {
        public int width;
        public int height;
        public int x;
        public int y;
        public float frameTime; // Time in seconds before switching to next frame
        public byte clearFlag; // 0 none, 1 clear to black, 2 set to previous frame
        public byte blendOperation; // 0 replace, 1 alpha blend

        public byte[] zlibHeaderData;
        public byte[] decompressedImageData;
    }

    public class AnimatedTextureQueueData {
	    public int adTextureId;
	    public string fileName;

	    public AnimatedTextureQueueData(int inAdTextureId, string inFileName) {
		    adTextureId = inAdTextureId;
		    fileName = inFileName;
	    }
    }
    
    public List<AnimatedTextureQueueData> animatedTextureLoadQueue = new List<AnimatedTextureQueueData>();

    public bool IsAnimatedTextureAlreadyInLoadQueue(int adTextureId) {
	    foreach(AnimatedTextureQueueData queueItem in animatedTextureLoadQueue)
		    if (queueItem.adTextureId == adTextureId)
			    return true;

	    return false;
    }

    private Coroutine activeAnimatedTextureLoadRoutine;
    
    public void LoadAnimatedTextures(int adTextureId, IASAdSize adSize, int adOffset) {
	    // If we've already loaded the animated textures for this ad this session then skip
	    if (advertTextures[adTextureId].animationFrames <= 0) {
		    // Returns true if the animated texture is currently loading or in queue to be loaded
		    if (IsAnimatedTextureAlreadyInLoadQueue(adTextureId)) {
			    // Do nothing, the animated texture is already loading or waiting in queue to be loaded
		    } else {
			    string fileName = GetAdFilename(adSize, adOffset);
			    
			    animatedTextureLoadQueue.Add(new AnimatedTextureQueueData(adTextureId, fileName));
			    
			    // If this is the only queue item, start loading the animated textures now
			    if (animatedTextureLoadQueue.Count == 1) {
				    AnimatedTextureQueueData queueItem = animatedTextureLoadQueue[0];
				    
				    string filePath = Application.persistentDataPath + Path.AltDirectorySeparatorChar;

				    activeAnimatedTextureLoadRoutine = StartCoroutine(AsyncLoadAnimatedTextures(queueItem.adTextureId, filePath, queueItem.fileName));
			    }
		    }
		    
	    } else {
		    if(OnAnimatedTexturesReady != null)
				OnAnimatedTexturesReady.Invoke(adTextureId);
	    }
    }

    private void DeleteAnimationFrames(string searchPattern) {
	    string[] animationFrameFiles = Directory.GetFiles(Application.persistentDataPath, searchPattern);

	    foreach (string animationFrameFilePath in animationFrameFiles) {
		    if(advancedLogging)
			    Debug.Log("Deleting old IAS frame to be rebuilt: " + animationFrameFilePath);
			
		    File.Delete(animationFrameFilePath);
	    }
    }
    
    private IEnumerator AsyncLoadAnimatedTextures(int adTextureId, string filePath, string fileName) {
	    if (File.Exists(filePath + fileName)) {
		    byte[] animatedTextureBytes = new byte[0];
		    
		    try {
			    // Read the APNG stored on disk into a byte array
			    animatedTextureBytes = File.ReadAllBytes(filePath + fileName);
		    } catch (IOException e) {
			    Debug.LogError	("Failed to read cached IAS texture " + fileName + " - " + e.Message);
			    yield break;
		    } catch (UnauthorizedAccessException e) {
			    Debug.LogError	("Failed to read cached IAS texture " + fileName + " - " + e.Message);
			    yield break;
		    }

		    if (animatedTextureBytes.Length > 0) {
			    yield return null;

			    // Check if all animation frames are already cached, then we don't need to rebuild them
			    GetBasicPNGInfo(animatedTextureBytes, out int animationFrames, out List<float> animationFrameTimes);
			    bool needToRebuildAnimationFrames = false;

			    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

			    for (int i = 0; i < animationFrames; i++) {
				    string curFrameFileName = fileNameWithoutExtension + "_frame_" + (i + 1) + ".png";

				    if (!File.Exists(filePath + curFrameFileName)) {
					    needToRebuildAnimationFrames = true;
					    break;
				    }
			    }

			    // If any generated frames are missing rebuild all animation frames
			    if (needToRebuildAnimationFrames) {
				    // Delete any existing cached animation frames as we're going to rebuild everything
				    DeleteAnimationFrames(fileNameWithoutExtension + "_frame_*");

				    // Iterate through the PNG byte data extracting data from the byte chunks
				    yield return StartCoroutine(ProcessPNGChunks(animatedTextureBytes));

				    // We're done building the frameByte list of data each frame, now generate separate PNG files from the data
				    for (int i = 0; i < frameData.Count; i++) {
					    FrameData curFrameData = frameData[i];
					    string curFrameFileName = fileNameWithoutExtension + "_frame_" + (i + 1) + ".png";
					    byte[] finalImageBytes = new byte[0];

					    // Step 1: Build the IHDR chunk (length, type, IHDR chunk, CRC)
					    // IDHR chunk: (width, height, bit depth, color type, compression method, filter method, interlace method)
					    byte[] chunkLength_IHDR = BitConverter.GetBytes((int) 13).Reverse().ToArray();
					    byte[] chunkType_IHDR = new byte[] {(byte) 'I', (byte) 'H', (byte) 'D', (byte) 'R'};
					    byte[] chunkData_IHDR = BitConverter.GetBytes(imageMetaData.width).Reverse().ToArray()
						    .Concat(BitConverter.GetBytes(imageMetaData.height).Reverse().ToArray())
						    .Concat(new byte[] {
							    imageMetaData.bitDepth,
							    imageMetaData.colorType,
							    imageMetaData.compressionMethod,
							    imageMetaData.filterMethod,
							    imageMetaData.interlaceMethod
						    }).ToArray();
					    byte[] chunkCRC_IHDR = CalculateChunkCRC(chunkType_IHDR, chunkData_IHDR);
					    byte[] chunk_IHDR = chunkLength_IHDR.Concat(chunkType_IHDR).Concat(chunkData_IHDR).Concat(chunkCRC_IHDR).ToArray();

					    // Step 2: If a color palette was defined build a PLTE chunk (length, type, PLTE chunk, CRC)
					    // PLTE chunk: (red, green, blue) repeated between 1 and 256 times
					    byte[] chunkLength_PLTE = BitConverter.GetBytes((int) imageMetaData.colorPaletteBytes.Length).Reverse().ToArray();
					    byte[] chunkType_PLTE = new byte[] {(byte) 'P', (byte) 'L', (byte) 'T', (byte) 'E'};
					    byte[] chunkData_PLTE = imageMetaData.colorPaletteBytes;
					    byte[] chunkCRC_PLTE = CalculateChunkCRC(chunkType_PLTE, chunkData_PLTE);
					    byte[] chunk_PLTE = chunkLength_PLTE.Concat(chunkType_PLTE).Concat(chunkData_PLTE).Concat(chunkCRC_PLTE).ToArray();

					    // Step 3: If transparency info was defined build a tRNS chunk (length, type, tRNS chunk, CRC)
					    // tRNS chunk: color type 0 (grey) / color type 2 (red, green, blue) / color type 3 (alpha 0, alpha 1, ..etc..)
					    byte[] chunkLength_tRNS = BitConverter.GetBytes((int) imageMetaData.transparencyInfoBytes.Length).Reverse().ToArray();
					    byte[] chunkType_tRNS = new byte[] {(byte) 't', (byte) 'R', (byte) 'N', (byte) 'S'};
					    byte[] chunkData_tRNS = imageMetaData.transparencyInfoBytes;
					    byte[] chunkCRC_tRNS = CalculateChunkCRC(chunkType_tRNS, chunkData_tRNS);
					    byte[] chunk_tRNS = chunkLength_tRNS.Concat(chunkType_tRNS).Concat(chunkData_tRNS).Concat(chunkCRC_tRNS).ToArray();

					    // Step 4: Build the IDAT chunk (length, type, zlib compressed image data, CRC)
					    byte[] chunkType_IDAT = new byte[] {(byte) 'I', (byte) 'D', (byte) 'A', (byte) 'T'};
					    byte[] chunkData_IDAT = new byte[0];

					    if (i > 0) {
						    // Check the clear flag of the previous frame
						    byte clearFlag = frameData[i - 1].clearFlag;

						    switch (clearFlag) {
							    default:
							    case (byte) 0: // Do nothing, just draw over the previous frame
								    chunkData_IDAT = new byte[frameData[i - 1].decompressedImageData.Length];
								    Array.Copy(frameData[i - 1].decompressedImageData, 0, chunkData_IDAT, 0, chunkData_IDAT.Length);
								    break;

							    case (byte) 1: // Reset the frame region to fully transparent black
								    chunkData_IDAT = new byte[frameData[i - 1].decompressedImageData.Length];
								    //Array.Copy(frameData[i - 1].decompressedImageData, 0, chunkData_IDAT, 0, chunkData_IDAT.Length);
								    Array.Clear(chunkData_IDAT, 0, chunkData_IDAT.Length);
								    break;

							    case (byte) 2: // Use the frame before the previous
								    byte prevClearFlag = (byte) 2;
								    int framesToJumpBack = -1;

								    while (prevClearFlag == 2) {
									    framesToJumpBack++;
									    prevClearFlag = frameData[i - (2 + framesToJumpBack)].clearFlag;
								    }

								    chunkData_IDAT = new byte[frameData[i - (2 + framesToJumpBack)].decompressedImageData.Length];
								    Array.Copy(frameData[i - (2 + framesToJumpBack)].decompressedImageData, 0, chunkData_IDAT, 0, chunkData_IDAT.Length); // TODO: What happens if multiple frames have clearFlag 2? should I keep iterating backwards
								    break;
						    }

						    // Blend and filter the new frame data onto the existing image
						    // this is how animated PNGs work, they don't just contain full frame image data
						    yield return StartCoroutine(ApplyFilteringAndBlending(imageMetaData, curFrameData, chunkData_IDAT));
					    } else {
						    // We don't need to make any modifications to the first frame
						    chunkData_IDAT = new byte[curFrameData.decompressedImageData.Length];
						    Array.Copy(curFrameData.decompressedImageData, 0, chunkData_IDAT, 0, curFrameData.decompressedImageData.Length);
					    }

					    curFrameData.decompressedImageData = chunkData_IDAT;

					    yield return null;

					    // zlib compress the image data and prepend the zlib header
					    byte[] chunkData_IDAT_Compressed = curFrameData.zlibHeaderData.Concat(CompressDatastream(chunkData_IDAT)).Concat(CalculatezlibChecksum(chunkData_IDAT)).ToArray();
					    byte[] chunkLength_IDAT = BitConverter.GetBytes((int) chunkData_IDAT_Compressed.Length).Reverse().ToArray();
					    byte[] chunkCRC_IDAT = CalculateChunkCRC(chunkType_IDAT, chunkData_IDAT_Compressed);
					    byte[] chunk_IDAT = chunkLength_IDAT.Concat(chunkType_IDAT).Concat(chunkData_IDAT_Compressed).Concat(chunkCRC_IDAT).ToArray();

					    yield return null;

					    // Step 5: Build the IEND chunk (length, type, blank data, CRC)
					    byte[] chunkLength_IEND = BitConverter.GetBytes((int) 0).Reverse().ToArray();
					    byte[] chunkType_IEND = new byte[] {(byte) 'I', (byte) 'E', (byte) 'N', (byte) 'D'};
					    byte[] chunkData_IEND = new byte[0];
					    byte[] chunkCRC_IEND = CalculateChunkCRC(chunkType_IEND, chunkData_IEND);
					    byte[] chunk_IEND = chunkLength_IEND.Concat(chunkType_IEND).Concat(chunkData_IEND).Concat(chunkCRC_IEND).ToArray();

					    finalImageBytes = pngHeader
						    .Concat(chunk_IHDR)
						    .Concat(imageMetaData.colorPaletteBytes.Length > 0 ? chunk_PLTE : new byte[0])
						    .Concat(chunk_tRNS)
						    .Concat(chunk_IDAT)
						    .Concat(chunk_IEND)
						    .ToArray();

					    try {
							// Write the final rebuild frame to disk as a separate image
							File.WriteAllBytes(filePath + curFrameFileName, finalImageBytes);
						} catch (IOException e) {
                            Debug.LogError("IAS failed to create cached frame texture - " + e.Message);
                        } catch (UnauthorizedAccessException e) {
                            Debug.LogError("IAS failed to create cached frame texture - " + e.Message);
                        }

					    yield return null;

					    Texture2D animationFrameTex = new Texture2D(2, 2, TextureFormat.ARGB32, false);

					    if (animationFrameTex.LoadImage(finalImageBytes, !compressLoadedTextures)) { // Expensive operation
						    if (compressLoadedTextures) {
							    // Make sure the cached texture is already POT sized
							    if (Mathf.IsPowerOfTwo(animationFrameTex.width) && Mathf.IsPowerOfTwo(animationFrameTex.height)) {
								    animationFrameTex.Compress(true);
							    } else {
								    Debug.LogError("Cached animation frame " + curFrameFileName + " is incorrectly sized for compression!");
							    }
						    }

						    // Add the current frame texture and frame time info to the animatedTextureFrames list
						    advertTextures[adTextureId].animatedTextureFrames.Add(new IASTextureData.IASAnimatedFrameData(animationFrameTex, curFrameData.frameTime));
					    } else {
						    Debug.LogError("Failed to load generated animation frame " + curFrameFileName + " into a Texture2D!");

						    // Destroy it from memory to fix ALLOC_TEMP_TLS
						    Destroy(animationFrameTex);
						    animationFrameTex = null;
					    }
				    }

				    advertTextures[adTextureId].animationFrames = advertTextures[adTextureId].animatedTextureFrames.Count;
			    } else {
				    for (int i = 0; i < animationFrames; i++) {
					    string curFrameFileName = fileNameWithoutExtension + "_frame_" + (i + 1) + ".png";

					    // Read the cached current frame stored on disk into a byte array
					    byte[] curFrameBytes = File.ReadAllBytes(filePath + curFrameFileName);

					    yield return null;
					    
					    Texture2D animationFrameTex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
					    if (animationFrameTex.LoadImage(curFrameBytes, !compressLoadedTextures)) { // Expensive operation
						    if (compressLoadedTextures) {
							    // Make sure the cached texture is already POT sized
							    if (Mathf.IsPowerOfTwo(animationFrameTex.width) && Mathf.IsPowerOfTwo(animationFrameTex.height)) {
								    animationFrameTex.Compress(true);
							    } else {
								    if (advancedLogging)
									    Debug.Log("Cached animation frame " + curFrameFileName + " cannot be compressed as it's not POT size!");
							    }
						    }
					    } else {
						    Debug.LogError("Failed to load animation frame from cache into a Texture2D!");
					    }

					    // Add the current frame texture and frame time info to the animatedTextureFrames list
					    advertTextures[adTextureId].animatedTextureFrames.Add(new IASTextureData.IASAnimatedFrameData(animationFrameTex, animationFrameTimes[i]));
				    }

				    advertTextures[adTextureId].animationFrames = animationFrames;
			    }

			    if (OnAnimatedTexturesReady != null)
				    OnAnimatedTexturesReady.Invoke(adTextureId);
		    } else {
			    Debug.LogError("Failed to read animated IAS texture " + fileName + " from disk!");
		    }
	    } else {
		    Debug.LogError("Animated IAS texture " + fileName + " doesn't exist on disk!");
	    }

	    animatedTextureLoadQueue.RemoveAt(0);

	    // Start processing the next animated texture in queue (if any)
	    if (animatedTextureLoadQueue.Count > 0) {
		    AnimatedTextureQueueData queueItem = animatedTextureLoadQueue[0];

		    activeAnimatedTextureLoadRoutine = StartCoroutine(AsyncLoadAnimatedTextures(queueItem.adTextureId, Application.persistentDataPath + Path.AltDirectorySeparatorChar, queueItem.fileName));
	    } else {
		    activeAnimatedTextureLoadRoutine = null;
	    }
    }

    // Quickly just get the frame count and frame timing so we can determine if the frames need to rebuilt
    private void GetBasicPNGInfo(byte[] animatedTextureBytes, out int totalFrames, out List<float> frameTimes) {
	    totalFrames = 0;
	    frameTimes = new List<float>();
	    
	    int offset = pngHeader.Length;
	    
	    byte[] byteChunkData = null; // Size varies per frame so we set this inside the chunk iteration
	    
	    while (offset < animatedTextureBytes.Length) {
		    // First 4 bytes are the length of the current chunk
		    int chunkLength = BitConverter.ToInt32(new ArraySegment<byte>(animatedTextureBytes, offset, 4).Reverse().ToArray(), 0);
		    offset += 4;

		    // Next 4 bytes is the type of the current chunk
		    string chunkType = Encoding.ASCII.GetString(new ArraySegment<byte>(animatedTextureBytes, offset, 4));
		    offset += 4;

		    // Next x bytes is the data of the current chunk (can be blank)
		    byteChunkData = new byte[chunkLength];
		    Buffer.BlockCopy(animatedTextureBytes, offset, byteChunkData, 0, chunkLength);
		    
		    // then followed by 4 bytes of CRC chunk
		    offset += chunkLength + 4;

		    if (chunkType == "fcTL") {
			    totalFrames++;
			    
			    int numerator = BitConverter.ToInt16(new ArraySegment<byte>(byteChunkData, 20, 2).Reverse().ToArray(), 0);
			    int denominator = BitConverter.ToInt16(new ArraySegment<byte>(byteChunkData, 22, 2).Reverse().ToArray(), 0);

			    frameTimes.Add((float) numerator / (float) (denominator <= 0 ? 100 : denominator));
		    }
	    }
    }

    private ImageMetaData imageMetaData;
    private List<FrameData> frameData = new List<FrameData>();
    
    private IEnumerator ProcessPNGChunks(byte[] animatedTextureBytes) {
	    imageMetaData = new ImageMetaData();
	    frameData.Clear();
	    
	    if (!animatedTextureBytes.Take(pngHeader.Length).SequenceEqual(pngHeader)) {
            Debug.LogError("Invalid PNG! The loaded image was not a valid PNG/APNG format.");
            yield break;
	    }

	    FrameData curFrameData = null;
        int offset = pngHeader.Length;

        byte[] byteChunkLength = new byte[4];
        byte[] byteChunkType = new byte[4];
        
        // Once we hit the IDAT chunk we need to append all IDAT chunks before decompressing it
        bool lastChunkWasIDAT = false;
        byte[] compressedIDAT = new byte[0];
        
        // Once we hit an fdAT chunk we need to append all fdAT chunks before decompressing it
        bool lastChunkWasfdAT = false;
        byte[] compressedfdAT = new byte[0];
        
        while (offset < animatedTextureBytes.Length) {
            // First 4 bytes are the length of the current chunk
            Buffer.BlockCopy(animatedTextureBytes, offset, byteChunkLength, 0, 4);
            int chunkLength = BitConverter.ToInt32(byteChunkLength.Reverse().ToArray(), 0);
            offset += 4;

            // Next 4 bytes is the type of the current chunk
            Buffer.BlockCopy(animatedTextureBytes, offset, byteChunkType, 0, 4);
            string chunkType = Encoding.ASCII.GetString(byteChunkType);
            offset += 4;

            // Next x bytes is the data of the current chunk (can be blank)
            byte[] byteChunkData = new byte[chunkLength];
	        Buffer.BlockCopy(animatedTextureBytes, offset, byteChunkData, 0, chunkLength);
            offset += chunkLength;

            // Next 4 bytes is the CRC of the current chunk
            //byte[] byteChunkCRC = animatedTextureBytes.Skip(offset).Take(4).ToArray();
            offset += 4;

            //Debug.Log("Reading chunk: " + chunkType + " (" + chunkLength + ")");

            // Valid animated PNGs should always have a frame control chunk before any data chunks (including initial IDAT)
            if (curFrameData == null && (chunkType == "IDAT" || chunkType == "fdAT")){
	            Debug.LogError("Invalid PNG! fcTL information missing before image data chunk!");
	            yield break;
            }

            if (lastChunkWasIDAT && chunkType != "IDAT") {
	            lastChunkWasIDAT = false;
	            
	            // The first 2 bytes of data is the zlib header containing zlib compression method and additional flags/check bits
	            curFrameData.zlibHeaderData = compressedIDAT.Take(2).ToArray();
	            
	            // We need to exclude the zlib header and footer from the data to decompress it
	            curFrameData.decompressedImageData = DecompressDatastream(compressedIDAT.Skip(2).Take(compressedIDAT.Length - 6).ToArray());
	            
	            // Reset the compressed IDAT byte array
	            compressedIDAT = new byte[0];
            }

            if (lastChunkWasfdAT && chunkType != "fdAT") {
	            lastChunkWasfdAT = false;
	            
	            // Skip the first 4 bytes which is the sequence number of fdAT
	            // then take the next 2 bytes which is the zlib header containing zlib compression method and additional flags/check bits
	            curFrameData.zlibHeaderData = compressedfdAT.Take(2).ToArray();

	            // Exclude the sequence number, zlib header and footer from the data to decompress it
	            curFrameData.decompressedImageData = DecompressDatastream(compressedfdAT.Skip(2).Take(compressedfdAT.Length - 6).ToArray());

	            // Reset the compressed fdAT byte array
	            compressedfdAT = new byte[0];
            }
            
            switch (chunkType) {
                case "IHDR": // Image header (required)
                    imageMetaData.width = BitConverter.ToInt32(byteChunkData.Take(4).Reverse().ToArray(), 0);
                    imageMetaData.height = BitConverter.ToInt32(byteChunkData.Skip(4).Take(4).Reverse().ToArray(), 0);
                    imageMetaData.bitDepth = byteChunkData.Skip(8).Take(1).ToArray()[0];
                    imageMetaData.colorType = byteChunkData.Skip(9).Take(1).ToArray()[0];
                    imageMetaData.compressionMethod = byteChunkData.Skip(10).Take(1).ToArray()[0];
                    imageMetaData.filterMethod = byteChunkData.Skip(11).Take(1).ToArray()[0];
                    imageMetaData.interlaceMethod = byteChunkData.Skip(12).Take(1).ToArray()[0];
                    break;

                case "PLTE": // Palette (optional)
                    imageMetaData.colorPaletteBytes = byteChunkData;
                    break;

                case "tRNS": // Transparency information (optional)
                    imageMetaData.transparencyInfoBytes = byteChunkData;
                    
                    // Non-transparent color modes use chroma key(s) to define transparency rather than defining transparency data per pixel
                    switch (imageMetaData.colorType) {
                        case (byte)0: // Grayscale chroma key (2 bytes)
                            imageMetaData.chromaKeyValues = new int[1];
                            imageMetaData.chromaKeyValues[0] = BitConverter.ToInt16(imageMetaData.transparencyInfoBytes.Take(2).Reverse().ToArray(), 0);
                            break;
                                    
                        case (byte)2: // RGB chroma key (2 bytes per value)
                            imageMetaData.chromaKeyValues = new int[3];
                            imageMetaData.chromaKeyValues[0] = BitConverter.ToInt16(imageMetaData.transparencyInfoBytes.Take(2).Reverse().ToArray(), 0);
                            imageMetaData.chromaKeyValues[1] = BitConverter.ToInt16(imageMetaData.transparencyInfoBytes.Skip(2).Take(2).Reverse().ToArray(), 0);
                            imageMetaData.chromaKeyValues[2] = BitConverter.ToInt16(imageMetaData.transparencyInfoBytes.Skip(4).Take(2).Reverse().ToArray(), 0);
                            break;
                                    
                        case (byte)3: // Palette index chroma keys (1 byte per value)
                            imageMetaData.chromaKeyValues = new int[imageMetaData.transparencyInfoBytes.Length];

                            for (int i = 0; i < imageMetaData.chromaKeyValues.Length; i++)
                                imageMetaData.chromaKeyValues[i] = (int) imageMetaData.transparencyInfoBytes[i];
                            break;
                    }
                    break;

                case "fcTL": // Frame control chunk (defines some information about the frame)
	                yield return null;
	                
	                // fcTL is called at the start of each new frame (including before the initial IDAT chunk)
	                frameData.Add(new FrameData());
	                curFrameData = frameData[frameData.Count - 1];

	                curFrameData.width = BitConverter.ToInt32(byteChunkData.Skip(4).Take(4).Reverse().ToArray(), 0);
	                curFrameData.height = BitConverter.ToInt32(byteChunkData.Skip(8).Take(4).Reverse().ToArray(), 0);
	                curFrameData.x = BitConverter.ToInt32(byteChunkData.Skip(12).Take(4).Reverse().ToArray(), 0);
	                curFrameData.y = BitConverter.ToInt32(byteChunkData.Skip(16).Take(4).Reverse().ToArray(), 0);

	                int numerator = BitConverter.ToInt16(byteChunkData.Skip(20).Take(2).Reverse().ToArray(), 0);
	                int denominator = BitConverter.ToInt16(byteChunkData.Skip(22).Take(2).Reverse().ToArray(), 0);

	                curFrameData.frameTime = (float) numerator / (float) (denominator <= 0 ? 100 : denominator);

	                // Frame disposal mode
	                // 0 = do nothing (render on top of contents on previous frame)
	                // 1 = frame is cleared to fully black before rendering next frame
	                // 2 = after rendering this frame revert to previous previous frame
	                curFrameData.clearFlag = byteChunkData.Skip(24).Take(1).ToArray()[0];

	                // Blend operation mode
	                // 0 = current frame just replaces existing pixels
	                // 1 = current frame is blended based on alpha
	                curFrameData.blendOperation = byteChunkData.Skip(25).Take(1).ToArray()[0];
	                break;
                
                case "IDAT": // Image data for first frame only (used for backwards support to PNG)
	                if (!lastChunkWasIDAT) {
		                curFrameData.width = imageMetaData.width;
		                curFrameData.height = imageMetaData.height;
		                curFrameData.x = 0;
		                curFrameData.y = 0;
		                lastChunkWasIDAT = true;
	                }

	                compressedIDAT = compressedIDAT.Concat(byteChunkData.Take(chunkLength)).ToArray();
                    break;

                case "fdAT": // Frame data for all frames after the first
	                if(!lastChunkWasfdAT)
						lastChunkWasfdAT = true;

	                // Skip the first 4 bytes which is the frame data sequence number
	                compressedfdAT = compressedfdAT.Concat(byteChunkData.Skip(4).Take(chunkLength - 4)).ToArray();
                    break;
            }
        }
    }
    
    private IEnumerator ApplyFilteringAndBlending(ImageMetaData imageMetaData, FrameData curFrameData, byte[] chunkData_IDAT) {
	    // The blend_op used for the current frame
        byte blendMethod = curFrameData.blendOperation;

        // The colour type used by the image (0 grayscale, 2 truecolor, 3 indexed-color, 4 grayscale with alpha, 6 truecolor with alpha)
        byte colorMethod = imageMetaData.colorType;
        
        // We need to handle pixel iteration different based on colour mode
        // Defines how many bytes a single pixel uses in the selected colour mode
        int bytesPerPixel = 1;

        switch (colorMethod) {
            case (byte) 0:
                bytesPerPixel = 1;
                break; // 0 - grayscale each byte is a grayscale pixel
            case (byte) 2:
                bytesPerPixel = 3;
                break; // 2 - truecolor bytes are R, G, B repeating
            case (byte) 3:
                bytesPerPixel = 1;
                break; // 3 - indexed each byte is a palette index for a separate pixel
            case (byte) 4:
                bytesPerPixel = 2;
                break; // 4 - grayscale with alpha - bytes are grayscale pixels then alpha repeating
            case (byte) 5:
                bytesPerPixel = 4;
                break; // 5 - truecolor with alpha - bytes are R, G, B, A repeating
        }
        
        // Current image data bytes from the source image data (the current frame) (amount of bytes depend on color mode)
        byte[] sourceBytes = new byte[bytesPerPixel];
        bool hasChromaKey = imageMetaData.chromaKeyValues != null && imageMetaData.chromaKeyValues.Length > 0;
        int byteLength = curFrameData.decompressedImageData.Length;
        
        // Iterate through the rows of data copying data row by row
        for (int row = curFrameData.y; row < curFrameData.y + curFrameData.height; row++) {
            // Pixel index in our destination image data byte array (the full image)
            int destIndex = row * ((bytesPerPixel * imageMetaData.width) + 1) + 1 + (bytesPerPixel * curFrameData.x);

            // Pixel index in our source image data byte array (the current frame)
            int sourceIndex = (row - curFrameData.y) * ((bytesPerPixel * curFrameData.width) + 1) + 1;

            // Break out of the for loop if our index is out of range of the byte length
            // (for some reason on android frames would randomly have less bytes than expected :s)
            if (sourceIndex >= byteLength) break;

            // Filter method the current row will use (none, sub, up, average, paeth)
            byte filterMethod = curFrameData.decompressedImageData[sourceIndex - 1];

            // Iterate through each pixel in the current row column by column
            for (int column = 0; column < curFrameData.width; column++) {
                int pixelColumn = bytesPerPixel * column;
                
                for (int byteIndex = 0; byteIndex < bytesPerPixel; byteIndex++) {
	                sourceBytes[byteIndex] = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex];

                    // Apply the filter method for the current row onto the current image data byte
                    switch (filterMethod) {
                        case (byte) 0: // None (no filtering)
                            // Don't need to do anything
                            break;

                        case (byte) 1: // Sub (Subtract the left byte from the current byte)
                            if (column >= 1) {
								byte leftByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - (bytesPerPixel * 1)];

                                sourceBytes[byteIndex] -= leftByte;
                            }
                            break;

                        case (byte) 2: // Up (Subtract the byte above from the current byte)
                            if (row >= 1) {
								byte upByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - ((bytesPerPixel * curFrameData.width) + 1)];

                                sourceBytes[byteIndex] -= upByte;
                            }
                            break;

                        case (byte) 3: // Average (Subtract (left byte + above byte) / 2 from the current byte)
                            if (column >= 1 && row >= 1) {
	                            byte leftByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - (bytesPerPixel * 1)];
                                byte upByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - ((bytesPerPixel * curFrameData.width) + 1)];

                                sourceBytes[byteIndex] -= (byte) ((leftByte + upByte) / 2);
                            }
                            break;

                        case (byte) 4: // Paeth (Subtract the left, above and top left bytes ran through the PaethPredictor algorithm)
                            if (column >= 1 && row >= 1) {
	                            byte leftByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - (bytesPerPixel * 1)];
	                            byte upByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - ((bytesPerPixel * curFrameData.width) + 1)];
                                byte upLeftByte = curFrameData.decompressedImageData[sourceIndex + pixelColumn + byteIndex - ((bytesPerPixel * curFrameData.width) + 1 + (bytesPerPixel * 1))];

                                sourceBytes[byteIndex] -= PaethPredictor(leftByte, upByte, upLeftByte);
                            }
                            break;
                    }
                }
                
                // Apply the blend_op to the current image data byte
                switch (blendMethod) {
                    case (byte) 0: // Pixel replace mode
	                    // Do nothing
                        break;

                    case (byte) 1: // Pixel alpha blend mode (APNG_BLEND_OP_OVER)
	                    // Get the current alpha value of the pixel from the current image data byte
	                    // If we're not in an alpha color mode then if tRNS is set then the color it defines will be used as transparency (like a chroma key)
	                    // Otherwise we fallback to 255 (full opacity)
	                    byte alpha = (byte) 255;
                
	                    switch (colorMethod) {
		                    case (byte) 0: // Grayscale, uses a single value chroma value
			                    // If the current pixel colour matches the chromaSample then it's transparent
			                    if (hasChromaKey) {
				                    if (sourceBytes[0] == imageMetaData.chromaKeyValues[0])
					                    alpha = (byte) 0;
			                    }
			                    break;

		                    case (byte) 2: // RGB, uses R, G, B chroma value
			                    if (hasChromaKey) {
				                    if (sourceBytes[0] == imageMetaData.chromaKeyValues[0] && sourceBytes[1] == imageMetaData.chromaKeyValues[1] && sourceBytes[2] == imageMetaData.chromaKeyValues[2])
					                    alpha = (byte) 0;
			                    }
			                    break;

		                    case (byte) 3: // Palette, uses an alpha value defined in each palette
			                    if (hasChromaKey) {
				                    if (imageMetaData.chromaKeyValues.Length > sourceBytes[0])
					                    alpha = (byte) imageMetaData.chromaKeyValues[sourceBytes[0]];
			                    }
			                    break;

		                    case (byte) 4: // Grayscale with an alpha value defined per pixel at index 1
			                    alpha = sourceBytes[1];
			                    break;

		                    case (byte) 6: // Truecolor with an alpha value defined per pixel at index 3
			                    alpha = sourceBytes[3];
			                    break;
	                    }
	                    
                        for (int byteIndex = 0; byteIndex < bytesPerPixel; byteIndex++) {
                            if (alpha == 0) {
                                // Fully transparent alpha, just keep the destination pixels
                                sourceBytes[byteIndex] = chunkData_IDAT[destIndex + pixelColumn + byteIndex];
                            } else if (alpha == 255) {
                                // Fully opaque, treat this as blend mode 0 (pixel replace mode)
                                // Don't need to do anything
                            } else {
                                // Indexed colour mode does not use APNG_BLEND_OP_OVER
                                if (colorMethod != 3) {
                                    // Blend the pixel using APNG_BLEND_OP_OVER
                                    byte destByte = chunkData_IDAT[destIndex + pixelColumn + byteIndex];
                                    float alphaRatio = alpha / 255f;

                                    sourceBytes[byteIndex] = (byte) (alphaRatio * sourceBytes[byteIndex] + (1 - alphaRatio) * (destByte | (0xff - destByte)));
                                }
                            }
                        }
                        break;
                }
                
                // Update the target image data byte in our destination with the updated source byte
                for (int byteIndex = 0; byteIndex < bytesPerPixel; byteIndex++)
	                chunkData_IDAT[destIndex + pixelColumn + byteIndex] = sourceBytes[byteIndex];
            }

            // Wait a frame every x rows to reduce single frame overhead
            if (row % 100 == 0) yield return null;
        }
    }

    // https://www.atalasoft.com/cs/blogs/stevehawley/archive/2010/02/23/libpng-you-re-doing-it-wrong.aspx
    private static byte PaethPredictor(byte a, byte b, byte c) {
        int bmc = b - c;
        int pa = bmc < 0 ? -bmc : bmc;
        int amc = a - c;
        int pb = amc < 0 ? -amc : amc;
        int apbmcmc = a + b - c - c;
        int pc = apbmcmc < 0 ? -apbmcmc : apbmcmc;
       
        return pa <= Math.Min(pa, pc) ? a : (pb <= pc ? b : c);
    }
    
    private byte[] DecompressDatastream(byte[] zlibBytes) {
	    // larger = faster but uses my memory (GC alloc)
	    // smaller = slower but lower memory ceiling (GC alloc)
	    int bufferSize = 8192;
	    byte[] buffer = new byte[bufferSize]; 
	    
        // We need to exclude the zlib header from the data to decompress it
        using (MemoryStream output = new MemoryStream()) {
	        using (MemoryStream compressedStream = new MemoryStream(zlibBytes)) {
		        using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress)) {
			        int bytesRead;

			        while ((bytesRead = deflateStream.Read(buffer, 0, bufferSize)) > 0) {
				        output.Write(buffer, 0, bytesRead);
			        }
		        }
	        }

	        return output.ToArray();
        }
    }
    
    private byte[] CompressDatastream(byte[] decompressedBytes) {
	    // larger = faster but uses my memory (GC alloc)
	    // smaller = slower but lower memory ceiling (GC alloc)
	    int bufferSize = 8192;
	    byte[] buffer = new byte[bufferSize]; 
	    
	    using (MemoryStream decompressedStream = new MemoryStream(decompressedBytes)) {
		    using (MemoryStream compressedStream = new MemoryStream()) {
			    using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Compress)) {
				    int bytesRead;
				    
				    while ((bytesRead = decompressedStream.Read(buffer, 0, bufferSize)) > 0) {
					    deflateStream.Write(buffer, 0, bytesRead);
				    }
				    
				    deflateStream.Flush();
				    deflateStream.Close();
			    }
			    
			    return compressedStream.ToArray();
		    }
	    }
    }

    private byte[] CalculatezlibChecksum(byte[] data) {
        uint a1 = 1, a2 = 0;
        foreach (byte b in data) {
            a1 = (a1 + b) % 65521;
            a2 = (a2 + a1) % 65521;
        }

        byte[] outputData = new byte[4];
        outputData[0] = (byte) (a2 >> 8);
        outputData[1] = (byte) a2;
        outputData[2] = (byte) (a1 >> 8);
        outputData[3] = (byte) a1;
        return outputData;
    }

    private byte[] CalculateChunkCRC(byte[] typeBytes, byte[] dataBytes) {
        byte[] input = typeBytes.Concat(dataBytes).ToArray();
        return BitConverter.GetBytes(CalculateCRC(input)).Reverse().ToArray();
    }
    
    public static uint CalculateCRC(byte[] input) {
	    UInt32 crc = input.Aggregate(
		    0xffffffff,
		    (current, t) => (current >> 8) ^ CrcTable[(current & 0xff) ^ t]);
	    crc ^= 0xffffffff;

	    return crc;
    }

    private static readonly UInt32[] CrcTable = {
        0x00000000, 0x77073096, 0xee0e612c, 0x990951ba,
        0x076dc419, 0x706af48f, 0xe963a535, 0x9e6495a3,
        0x0edb8832, 0x79dcb8a4, 0xe0d5e91e, 0x97d2d988,
        0x09b64c2b, 0x7eb17cbd, 0xe7b82d07, 0x90bf1d91,
        0x1db71064, 0x6ab020f2, 0xf3b97148, 0x84be41de,
        0x1adad47d, 0x6ddde4eb, 0xf4d4b551, 0x83d385c7,
        0x136c9856, 0x646ba8c0, 0xfd62f97a, 0x8a65c9ec,
        0x14015c4f, 0x63066cd9, 0xfa0f3d63, 0x8d080df5,
        0x3b6e20c8, 0x4c69105e, 0xd56041e4, 0xa2677172,
        0x3c03e4d1, 0x4b04d447, 0xd20d85fd, 0xa50ab56b,
        0x35b5a8fa, 0x42b2986c, 0xdbbbc9d6, 0xacbcf940,
        0x32d86ce3, 0x45df5c75, 0xdcd60dcf, 0xabd13d59,
        0x26d930ac, 0x51de003a, 0xc8d75180, 0xbfd06116,
        0x21b4f4b5, 0x56b3c423, 0xcfba9599, 0xb8bda50f,
        0x2802b89e, 0x5f058808, 0xc60cd9b2, 0xb10be924,
        0x2f6f7c87, 0x58684c11, 0xc1611dab, 0xb6662d3d,
        0x76dc4190, 0x01db7106, 0x98d220bc, 0xefd5102a,
        0x71b18589, 0x06b6b51f, 0x9fbfe4a5, 0xe8b8d433,
        0x7807c9a2, 0x0f00f934, 0x9609a88e, 0xe10e9818,
        0x7f6a0dbb, 0x086d3d2d, 0x91646c97, 0xe6635c01,
        0x6b6b51f4, 0x1c6c6162, 0x856530d8, 0xf262004e,
        0x6c0695ed, 0x1b01a57b, 0x8208f4c1, 0xf50fc457,
        0x65b0d9c6, 0x12b7e950, 0x8bbeb8ea, 0xfcb9887c,
        0x62dd1ddf, 0x15da2d49, 0x8cd37cf3, 0xfbd44c65,
        0x4db26158, 0x3ab551ce, 0xa3bc0074, 0xd4bb30e2,
        0x4adfa541, 0x3dd895d7, 0xa4d1c46d, 0xd3d6f4fb,
        0x4369e96a, 0x346ed9fc, 0xad678846, 0xda60b8d0,
        0x44042d73, 0x33031de5, 0xaa0a4c5f, 0xdd0d7cc9,
        0x5005713c, 0x270241aa, 0xbe0b1010, 0xc90c2086,
        0x5768b525, 0x206f85b3, 0xb966d409, 0xce61e49f,
        0x5edef90e, 0x29d9c998, 0xb0d09822, 0xc7d7a8b4,
        0x59b33d17, 0x2eb40d81, 0xb7bd5c3b, 0xc0ba6cad,
        0xedb88320, 0x9abfb3b6, 0x03b6e20c, 0x74b1d29a,
        0xead54739, 0x9dd277af, 0x04db2615, 0x73dc1683,
        0xe3630b12, 0x94643b84, 0x0d6d6a3e, 0x7a6a5aa8,
        0xe40ecf0b, 0x9309ff9d, 0x0a00ae27, 0x7d079eb1,
        0xf00f9344, 0x8708a3d2, 0x1e01f268, 0x6906c2fe,
        0xf762575d, 0x806567cb, 0x196c3671, 0x6e6b06e7,
        0xfed41b76, 0x89d32be0, 0x10da7a5a, 0x67dd4acc,
        0xf9b9df6f, 0x8ebeeff9, 0x17b7be43, 0x60b08ed5,
        0xd6d6a3e8, 0xa1d1937e, 0x38d8c2c4, 0x4fdff252,
        0xd1bb67f1, 0xa6bc5767, 0x3fb506dd, 0x48b2364b,
        0xd80d2bda, 0xaf0a1b4c, 0x36034af6, 0x41047a60,
        0xdf60efc3, 0xa867df55, 0x316e8eef, 0x4669be79,
        0xcb61b38c, 0xbc66831a, 0x256fd2a0, 0x5268e236,
        0xcc0c7795, 0xbb0b4703, 0x220216b9, 0x5505262f,
        0xc5ba3bbe, 0xb2bd0b28, 0x2bb45a92, 0x5cb36a04,
        0xc2d7ffa7, 0xb5d0cf31, 0x2cd99e8b, 0x5bdeae1d,
        0x9b64c2b0, 0xec63f226, 0x756aa39c, 0x026d930a,
        0x9c0906a9, 0xeb0e363f, 0x72076785, 0x05005713,
        0x95bf4a82, 0xe2b87a14, 0x7bb12bae, 0x0cb61b38,
        0x92d28e9b, 0xe5d5be0d, 0x7cdcefb7, 0x0bdbdf21,
        0x86d3d2d4, 0xf1d4e242, 0x68ddb3f8, 0x1fda836e,
        0x81be16cd, 0xf6b9265b, 0x6fb077e1, 0x18b74777,
        0x88085ae6, 0xff0f6a70, 0x66063bca, 0x11010b5c,
        0x8f659eff, 0xf862ae69, 0x616bffd3, 0x166ccf45,
        0xa00ae278, 0xd70dd2ee, 0x4e048354, 0x3903b3c2,
        0xa7672661, 0xd06016f7, 0x4969474d, 0x3e6e77db,
        0xaed16a4a, 0xd9d65adc, 0x40df0b66, 0x37d83bf0,
        0xa9bcae53, 0xdebb9ec5, 0x47b2cf7f, 0x30b5ffe9,
        0xbdbdf21c, 0xcabac28a, 0x53b39330, 0x24b4a3a6,
        0xbad03605, 0xcdd70693, 0x54de5729, 0x23d967bf,
        0xb3667a2e, 0xc4614ab8, 0x5d681b02, 0x2a6f2b94,
        0xb40bbe37, 0xc30c8ea1, 0x5a05df1b, 0x2d02ef8d
    };
	
#if UNITY_EDITOR
	[ContextMenu("Open IAS GitHub URL")]
	private void OpenIASGithub() {
		Application.OpenURL("https://github.com/Seanm07/IAS-Standalone/");
	}

	private IEnumerator CheckIASVersion() {
		UnityWebRequest versionCheck = UnityWebRequest.Get("https://data.i6.com/IAS/ias_check.txt");

		yield return versionCheck.SendWebRequest();

		if (versionCheck.result != UnityWebRequest.Result.Success) {
			Debug.LogError("Error checking for IAS updates! " + versionCheck.error);
			yield break;
		}

		int latestVersion = 0;

		int.TryParse(versionCheck.downloadHandler.text, out latestVersion);

		if (latestVersion > internalScriptVersion) {
			if (EditorUtility.DisplayDialog("IAS Update Available!", "There's a new version of the IAS script available!\nWould you like to update now?\n\nIAS files will be automatically replaced with their latest versions!", "Yes", "No")) {
				string scriptPath = EditorUtility.OpenFilePanel("Select IAS_Manager.cs from your project!", "", "cs");

				if (scriptPath.Length > 0) {
					// Remove assets from the path because Unity 5.4.x has a bug where the return value of the path doesn't include assets unlike other versions of unity
					scriptPath = scriptPath.Replace("Assets/", "");

					// Re-add Assets/ but also remove the data path so the path starts at Assets/
					scriptPath = scriptPath.Replace(Application.dataPath.Replace("Assets", ""), "Assets/");

					UnityWebRequest scriptDownload = UnityWebRequest.Get("https://data.i6.com/IAS/GamePickle/IAS_Manager.cs");

					yield return scriptDownload.SendWebRequest();

					if (scriptDownload.result == UnityWebRequest.Result.Success) {
						FileStream tmpFile = File.Create(scriptPath + ".tmp");
						FileStream backupFile = File.Create(scriptPath + ".backup" + internalScriptVersion);

						tmpFile.Close();
						backupFile.Close();

						File.WriteAllText(scriptPath + ".tmp", scriptDownload.downloadHandler.text);
						File.Replace(scriptPath + ".tmp", scriptPath, scriptPath + ".backup");
						File.Delete(scriptPath + ".tmp");

						// Update the AssetDatabase so we can see the file changes in Unity
						AssetDatabase.Refresh();

						Debug.Log("IAS upgraded from version " + internalScriptVersion + " to " + latestVersion);

						// Force exit play mode
						EditorApplication.isPlaying = false;
					} else {
						Debug.LogError("Error downloading script update: " + scriptDownload.error);
					}
				} else {
					Debug.LogError("Update cancelled! Did not select the IAS_Manager.cs script!");
				}
			} else {
				Debug.LogError("Update cancelled! Make sure to update your IAS version before sending a build!");
			}
		}
	}
#endif

#if UNITY_ANDROID
	private void UpdateInstalledPackages() {
		if (advancedLogging)
			Debug.Log("IAS Updating Installed Packages");

		installedApps.Clear();

		// Get all installed packages with a bundleId matching our filter
		string filteredPackageListPickle = PickleCore.GetPackageList("com.pickle.");
		string filteredPackageListGumdrop = PickleCore.GetPackageList("com.gumdropgames.");

		int installedGamesCount = 0;

		// Added a length check because I can't remember if there's just a comma or 2 spaces and a comma if the list is empty
		string filteredPackageList = (filteredPackageListPickle.Length >= 3 ? filteredPackageListPickle : "") + filteredPackageListGumdrop;
		//string filteredPackageList = "com.pickle.PoliceCarlDriftSimulator, com.pickle.CopDutyPoliceCarSimulator";//", com.pickle.PoliceMotorbikeSimulator3D, com.pickle.ambulancedrivingsimulator";

		// Cleanup the package list mistakes (ending comma or any spaces)
		if (!string.IsNullOrEmpty(filteredPackageList)) {
			filteredPackageList = filteredPackageList.Trim(); // Trim whitespaces

			if (filteredPackageList.Length > 0) {
				filteredPackageList = filteredPackageList.Remove(filteredPackageList.Length - 1); // Remove the unwanted comma at the end of the list
				installedGamesCount = filteredPackageList.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Length;
			}

			// Split the list into a string array
			string[] packageArray = filteredPackageList.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

			if (packageArray.Length > 0) {
				// Extract all packages and store them in the installedApps list
				foreach (string packageName in packageArray) {
					installedApps.Add(packageName.Trim().ToLowerInvariant());
				}
			} else {
				if (advancedLogging)
					Debug.Log("No other installed packages found matching filter!");
			}
		} else {
			if (advancedLogging)
				Debug.Log("Filtered package list was empty!");
		}

		if (!PlayerPrefs.HasKey("IASTotalGamesLogged")) {
			FirebaseAnalyticsManager.SetUserProperty("pickle_games_installed", installedGamesCount.ToString());

			PlayerPrefs.SetInt("IASTotalGamesLogged", 1);
		}
	}
#endif

	void Awake() {
		// Destroy if this already exists
		if (instance) {
			Destroy(this);
			return;
		}

		instance = instance ?? this;

		bundleId = Application.identifier;
		appVersion = Application.version;

#if UNITY_EDITOR
		StartCoroutine(CheckIASVersion());
#endif

		CrossPlatformManager.OnStoreInitializeFinished += OnStoreInitializeFinished;
	}

	// Add a few frames of delay so we're not initializing alongside other scripts
	private void OnStoreInitializeFinished() {
		StartCoroutine(DoStoreInitialization());
	}

	private IEnumerator DoStoreInitialization() {
		for (int i = 0; i < 20; i++)
			yield return null;

		StoreInitializeFinished();
	}

	public void StoreInitializeFinished() {
		CrossPlatformManager.OnStoreInitializeFinished -= StoreInitializeFinished;

		AppStore store = CrossPlatformManager.GetActiveStore();

		// The editor will either act like the apple app store or google play store depending on ios or android build target
		if (store == AppStore.fake) {
#if UNITY_IOS
				store = AppStore.AppleAppStore;
#else
			store = AppStore.GooglePlay;
#endif
		}

		switch (store) {
			case AppStore.AppleAppStore:
			case AppStore.MacAppStore:
				// Slot 3 iOS Game Shark / slot 9 iOS freeonlinegames.com
				activeSlotId = bundleId.Contains("com.pickle.") ? 3 : 9;
				break;

			case AppStore.AmazonAppStore:
				// 2 = Amazon Standard / 22 = Amazon TV
				activeSlotId = PickleCore.IsAndroidTV() ? 22 : 2;
				break;

			case AppStore.GooglePlay:
				// (Game Pickle) 1 = Google Play Standard / 6 = Google Play Android TV
				activeSlotId = PickleCore.IsAndroidTV() ? 6 : 1;
				break;

			// Other stores fallback to no IAS ads as most stores do not allow linking to other stores
			default:
				activeJsonURL = "";
				break;
		}
		
		activeJsonURL = "https://ias.gamepicklestudios.com/ad/" + activeSlotId + ".json";

		if (!string.IsNullOrEmpty(activeJsonURL)) {
			storeSupportsIAS = true;

			Debug.Log("IAS Init [" + internalScriptVersion + "] " + bundleId + " (" + appVersion + ") (" + activeJsonURL.Substring(activeJsonURL.LastIndexOf('/') + 1) + ")");

#if UNITY_ANDROID
			// Get a list of installed packages on the device and store ones matching a filter
			UpdateInstalledPackages();
#endif

			bool cachedIASDataLoaded = LoadIASData();

			StartCoroutine(DownloadIASData(cachedIASDataLoaded));
		} else {
			storeSupportsIAS = false;

			Debug.Log("IAS not supported on this app store!");
		}
	}

	public void Update() {
		if (!storeSupportsIAS)
			return;

		if (framesUntilIASSave > 0) {
			framesUntilIASSave--;

			if (framesUntilIASSave == 0)
				SaveIASData(true);
		}
	}


	private void RefreshActiveAdSlots() {
		if (!storeSupportsIAS)
			return;
		
		if(advancedLogging)
			Debug.Log("Refreshing active ad slots..");
		
		for (int i = 1; DoesSlotIntExist((IASAdSize)i); i++) {
			RefreshBanners((IASAdSize) i);
		}
	}

	private void RefreshActiveAdSlots(AdJsonFileData customData = null) {
		if (!storeSupportsIAS)
			return;

		for (int i = 1; DoesSlotIntExist((IASAdSize)i, customData); i++)
			RefreshBanners((IASAdSize) i, false, customData);
	}

	private void RandomizeAdSlots(AdJsonFileData customData = null) {
		if (!storeSupportsIAS)
			return;

		for (int i = 1; DoesSlotIntExist((IASAdSize)i); i++) {
			AdSlotData curSlotData = GetAdSlotData((IASAdSize)i, customData);

			curSlotData.lastSlotId = UnityEngine.Random.Range(0, curSlotData.advert.Count - 1);
			curSlotData.lastBackscreenId = curSlotData.lastSlotId + 1 >= curSlotData.advert.Count ? 0 : curSlotData.lastSlotId + 1;
		}
	}

	// Used when we need to modify the values of AdJsonFileData temporarily without changing source values
	private AdJsonFileData DeepCopyAsJsonFileData(AdJsonFileData input) {
		AdJsonFileData curInputJsonFile = input;
		AdJsonFileData curOutputJsonFile = new AdJsonFileData();

#if UNITY_EDITOR
		curOutputJsonFile.name = ConvertToSecureProtocol(activeJsonURL);
#endif

		curOutputJsonFile.slotInts = new List<AdSlotData>();
		
		// Slot IDs determine ad sizes and are the numbers in the slots 1a, 1b, 2a etc
		for (int slotId = 0; slotId < curInputJsonFile.slotInts.Count; slotId++) {
			AdSlotData curInputSlot = curInputJsonFile.slotInts[slotId];

			curOutputJsonFile.slotInts.Add(new AdSlotData());

			AdSlotData curOutputSlot = curOutputJsonFile.slotInts[slotId];

#if UNITY_EDITOR
			curOutputSlot.name = "Slot " + curInputJsonFile.slotInts[slotId].slotInt;
#endif

			curOutputSlot.slotInt = curInputSlot.slotInt;
			curOutputSlot.lastSlotId = curInputSlot.lastSlotId;
			curOutputSlot.lastBackscreenId = curInputSlot.lastBackscreenId;

			curOutputSlot.advert = new List<AdData>();
			
			// Ad IDs determine the ads within the slots of sizes, they're the characters in the slots 1a, 1b, 1a etc
			for (int adId = 0; adId < curInputSlot.advert.Count; adId++) {
				AdData curInputAdvert = curInputSlot.advert[adId];

				curOutputSlot.advert.Add(new AdData());

				AdData curOutputAdvert = curOutputSlot.advert[adId];

#if UNITY_EDITOR
				curOutputAdvert.name = curInputJsonFile.slotInts[slotId].slotInt + curInputAdvert.slotChar.ToString();
#endif

				curOutputAdvert.adUniqueId = curInputAdvert.adUniqueId;
				curOutputAdvert.slotChar = curInputAdvert.slotChar;
				curOutputAdvert.hasAnimatedImage = curInputAdvert.hasAnimatedImage;
				curOutputAdvert.fileName = curInputAdvert.fileName;
				curOutputAdvert.isTextureFileCached = curInputAdvert.isTextureFileCached;
				curOutputAdvert.isTextureReady = curInputAdvert.isTextureReady;
				curOutputAdvert.isInstalled = curInputAdvert.isInstalled;
				curOutputAdvert.isSelf = curInputAdvert.isSelf;
				curOutputAdvert.isActive = curInputAdvert.isActive;
				curOutputAdvert.isDownloading = curInputAdvert.isDownloading;
				curOutputAdvert.lastUpdated = curInputAdvert.lastUpdated;
				curOutputAdvert.newUpdateTime = curInputAdvert.newUpdateTime;
				curOutputAdvert.imgUrl = ConvertToSecureProtocol(curInputAdvert.imgUrl);
				curOutputAdvert.adUrl = ConvertToSecureProtocol(curInputAdvert.adUrl);
				curOutputAdvert.packageName = curInputAdvert.packageName;
				curOutputAdvert.adTextureId = curInputAdvert.adTextureId;
			}
		}

		return curOutputJsonFile;
	}

	private string ConvertToSecureProtocol(string inputURL) {
		// C# internally checks the replace with indexOf anyway so no need to wrap with contains
		inputURL = inputURL.Replace("http://", "https://");

		return inputURL;
	}

	private string EncodeIASData() {
		try {
			//AdJsonFileData saveReadyAdvertData = DeepCopyAsJsonFileData(advertData);

			// Some parts of the data needs their values changing as they won't be valid for future sessions
			/*foreach (AdSlotData curSlotData in saveReadyAdvertData.slotInts) {
				foreach (AdData curData in curSlotData.advert) {
					curData.adTextureId = -1;
					curData.isTextureReady = false;
					curData.isDownloading = false;
					curData.lastUpdated = 0L;
				}
			}*/

			BinaryFormatter binaryData = new BinaryFormatter();
			MemoryStream memoryStream = new MemoryStream();

			// Serialize our data list into the memory stream
			binaryData.Serialize(memoryStream, (object) advertData);

			string base64Data = string.Empty;

			try {
				// Convert the buffer of the memory stream (the serialized object) into a base 64 string
				base64Data = Convert.ToBase64String(memoryStream.GetBuffer());
			} catch (FormatException e) {

				Debug.LogError("IAS mStream corrupt - " + e.Message);
				throw;
			}

			return base64Data;
		} catch (SerializationException e) {
			Debug.LogError("IAS encode fail - " + e.Message);
			throw;
		}
	}

	private AdJsonFileData DecodeIASData(string rawBase64Data) {
		try {
			BinaryFormatter binaryData = new BinaryFormatter();
			MemoryStream memoryStream = new MemoryStream(Convert.FromBase64String(rawBase64Data));

			try {
				AdJsonFileData loadedAdData = (AdJsonFileData) binaryData.Deserialize(memoryStream);
				
				foreach (AdSlotData curSlotData in loadedAdData.slotInts) {
					foreach (AdData curData in curSlotData.advert) {
						curData.adTextureId = -1;
						curData.isTextureReady = false;
						curData.isDownloading = false;
						//curData.lastUpdated = 0L;
					}
				}

				return loadedAdData;
			} catch (SerializationException e) {
				Debug.Log("IAS data format has changed - it will be rebuilt");
				return null;
			}
		} catch (FormatException e) {
			Debug.LogError("IAS data corrupt - it will be rebuilt | " + e.Message);
			return null;
		}
	}

	private void SaveIASData(bool forceSave = false) {
		if (!forceSave) {
			// If the save function is called multiple times within 200 frames it'll just reset the timer before the save happens
			// Unless forceSave is true which either means the user to quitting the app or framesUntilIASSave is 0
			framesUntilIASSave = 200;
			return;
		} else {
			framesUntilIASSave = -1;
		}

		// Make sure the advertData has actually been setup before trying to save it
		if (advertData != null) {
			string iasData = EncodeIASData();

			PlayerPrefs.SetString("IASAdvertData", iasData);
		}
	}

	private bool LoadIASData() {
		string loadedIASData = PlayerPrefs.GetString("IASAdvertData", string.Empty);

		if (!string.IsNullOrEmpty(loadedIASData)) {
			advertData = DecodeIASData(loadedIASData);

			if (advertData != null)
				return true;
		}
		
		advertData = new AdJsonFileData();
		return false;
	}

	private bool IsPackageInstalled(string packageName) {
		foreach (string comparisonApp in installedApps)
			if (packageName.ToLowerInvariant().Contains(comparisonApp))
				return true;

		return false;
	}

	private bool DoesSlotIntExist(IASAdSize adSize, AdJsonFileData customData = null) {
		return GetAdSlotData(adSize, customData) != null;
	}

	private bool DoesSlotCharExist(IASAdSize adSize, char wantedSlotChar, AdJsonFileData customData = null) {
		return GetAdDataByChar(adSize, wantedSlotChar, customData) != null;
	}

	private int GetSlotIndex(IASAdSize adSize, AdJsonFileData customData = null) {
		if ((customData != null ? customData.slotInts != null : advertData.slotInts != null)) {
			// Iterate through each slot in the requested json file
			for (int i = 0; i < (customData != null ? customData.slotInts.Count : advertData.slotInts.Count); i++) {
				AdSlotData curSlotData = (customData != null ? customData.slotInts[i] : advertData.slotInts[i]);

				// Check if this ad slot int matched the one we requested
				if (curSlotData.slotInt == (int)adSize)
					return i;
			}
		}

		return -1;
	}

	private int GetAdIndex(IASAdSize adSize, char wantedSlotChar, AdJsonFileData customData = null) {
		AdSlotData slotData = GetAdSlotData(adSize, customData);

		if (slotData.advert != null) {
			for (int i = 0; i < slotData.advert.Count; i++) {
				AdData curAdData = slotData.advert[i];

				if (wantedSlotChar == curAdData.slotChar)
					return i;
			}
		}

		return -1;
	}

	private AdSlotData GetAdSlotData(IASAdSize adSize, AdJsonFileData customData = null) {
		if ((customData != null ? customData.slotInts != null : advertData.slotInts != null)) {
			// Iterate through each slot in the requested json file
			foreach (AdSlotData curSlotData in (customData != null ? customData.slotInts : advertData.slotInts)) {
				// Check if this ad slot int matches the one we requested
				if (curSlotData.slotInt == (int)adSize)
					return curSlotData;
			}
		}

		return null;
	}

	public AdData GetAdData(IASAdSize adSize, int offset, AdJsonFileData customData = null) {
		return GetAdDataByChar(adSize, GetSlotChar(adSize, offset, customData), customData);
	}

	// This was originally just an override of the above function but in Unity 4 char is treated as an int which confuses Unity on which function to use..
	private AdData GetAdDataByChar(IASAdSize adSize, char wantedSlotChar, AdJsonFileData customData = null) {
		AdSlotData curAdSlotData = GetAdSlotData(adSize, customData);

		if (curAdSlotData != null) {
			foreach (AdData curData in curAdSlotData.advert) {
				// Check if this ad slot character matches the one we requested
				if (curData.slotChar == wantedSlotChar)
					return curData;
			}
		}

		return null;
	}

	private void IncSlotChar(IASAdSize adSize, AdJsonFileData customData = null) {
		// Exit early if the wanted slot is blacklisted
		foreach (IASAdSize blacklistedSlot in blacklistedSlots)
			if (adSize == blacklistedSlot)
				return;

		AdSlotData wantedSlotData = GetAdSlotData(adSize, customData);

		if (customData == null) {
			// Calculate the next valid slot char to be displayed
			char wantedSlotChar = GetSlotChar(adSize, 0, customData);
			
			wantedSlotData.lastSlotId = (((int) wantedSlotChar) - slotIdDecimalOffset);
			
			char wantedSlotCharBackscreen = GetSlotChar(adSize, 1, customData);
			
			wantedSlotData.lastBackscreenId = (((int) wantedSlotCharBackscreen) - slotIdDecimalOffset);
		}

		StartCoroutine(DownloadAdTexture(adSize));
	}

	private int GetMaxAdOffset(IASAdSize adSize) {
		switch (adSize) {
			case IASAdSize.Square: return squareBackscreenAds;
			case IASAdSize.Tall: return tallBackscreenAds;
		}

		return 0;
	}

	private IEnumerator DownloadAdTexture(IASAdSize adSize) {
		// Wait a frame just so calls to load textures aren't running instantly at app launch
		yield return null;

		int maxAdOffset = GetMaxAdOffset(adSize);

		if(advancedLogging)
			Debug.Log(adSize + " slot will load " + maxAdOffset + " extra ads - found " + advertData.slotInts[(int)adSize - 1].advert.Count + " total ads");

		bool allowSelfAdvertising = maxAdOffset >= advertData.slotInts[(int)adSize - 1].advert.Count;
		
		// We only need to preload ads for slotInt 1 which is the square ads for the backscreen
		for (int i = 0; i < maxAdOffset + 1 && (i < advertData.slotInts[(int)adSize - 1].advert.Count); i++) {
			int curAdTextureInsertId = -1;
			
			char slotChar = GetSlotChar(adSize, i, null);
			AdData curAdData = GetAdDataByChar(adSize, slotChar);

			if (advancedLogging)
				Debug.Log("Request to load " + (int)adSize + "" + slotChar + " index " + i);

			if (curAdData != null) {
				// Download the texture for the newly selected IAS advert
				// Only bother re-downloading the image if the timestamp has changed or the texture isn't marked as ready
				if (!curAdData.isTextureReady || curAdData.lastUpdated < curAdData.newUpdateTime) {
					// Check if this is an advert we may be using in this game
					// Note: We still download installed ads because we might need them if there's no ads to display
					if ((allowSelfAdvertising || !curAdData.isSelf) && curAdData.isActive) {
						// Whilst we still have wwwImage write the bytes to disk to save on needing extra operations
						string filePath = Application.persistentDataPath + Path.AltDirectorySeparatorChar;

						string fileName = "IAS_" + curAdData.fileName;
						
						// Set this info before downloading
						curAdData.lastUpdated = curAdData.newUpdateTime;
						curAdData.isDownloading = true;

						// Check to see if we have this advert locally cached
						if (curAdData.isTextureFileCached) {
							if (advancedLogging)
								Debug.Log("Starting ad download of " + curAdData.packageName + " " + (int)adSize + slotChar + " (CACHED)");

							// Make sure the cache file actually exists (unexpected write fails or manual deletion)
							if (File.Exists(filePath + fileName)) {
								if (advancedLogging)
									Debug.Log("Found cached ad image for " + (int)adSize + slotChar);

								yield return null;

								try {
									// Read the saved texture from disk
									byte[] imageData = File.ReadAllBytes(filePath + fileName);

									// We need to create a template texture, we're also setting the compression type here
									Texture2D imageTexture = new Texture2D(2, 2, TextureFormat.ARGB32, false);

#if UNITY_EDITOR
									imageTexture.name = (int) adSize + slotChar.ToString() + " - " + ConvertToSecureProtocol(activeJsonURL);
#endif

									// Load the image data, this will also resize the texture
									if (imageTexture.LoadImage(imageData, !compressLoadedTextures)) {
										// Make sure the cached texture is already POT sized
										if (!Mathf.IsPowerOfTwo(imageTexture.width) || !Mathf.IsPowerOfTwo(imageTexture.height)) {
											// Disabled: No longer resizing and compressing legacy images as it affected quality too much to be usable
											// This is probably a cached image from an old version of IAS
											//TextureScale.Scale(imageTexture, Mathf.ClosestPowerOfTwo(imageTexture.width / 2), Mathf.ClosestPowerOfTwo(imageTexture.height / 2));

											// Rewrite the scaled image to disk
											//File.WriteAllBytes(filePath + fileName, imageTexture.EncodeToPNG());
										} else {
											if (compressLoadedTextures)
												imageTexture.Compress(true);
										}

										advertTextures.Add(new IASTextureData(imageTexture));
										adTextureInsertId++;
										curAdTextureInsertId = adTextureInsertId;

										if (advancedLogging)
											Debug.Log("Ad texture added for " + (int) adSize + slotChar);
									} else {
										Debug.LogError("IAS Failed to load cached ad " + (int) adSize + slotChar + " from cache (Corrupted?) - Ad will be re-downloaded");

										curAdData.isTextureFileCached = false;

										// Re-download this file because the cached file is broken
										i--;
										continue;
									}
								} catch (IOException e) {
									Debug.Log("IAS Failed to load cached ad " + (int) adSize + slotChar + " - " + e.Message + " - Ad will be re-downloaded");

									curAdData.isTextureFileCached = false;

									// Re-download this file because the cached file is broken
									i--;
									continue;
								} catch (UnauthorizedAccessException e) {
									Debug.Log("IAS Failed to load cached ad (UnauthorizedAccessException) " + (int) adSize + slotChar + " - " + e.Message + " - Ad will be re-downloaded");

									curAdData.isTextureFileCached = false;

									// Re-download this file because the cached file is unusable
									i--;
									continue;
								}
							} else {
								if(advancedLogging)
									Debug.Log("Cached file " + filePath + fileName + " does not exist! (Re-requesting image download)");
								
								curAdData.isTextureFileCached = false;

								// Retry the download now that we know the cached image is missing
								i--;
								continue;
							}
						} else {
							if (advancedLogging)
								Debug.Log("Starting ad download of " + curAdData.packageName + " " + (int)adSize + slotChar + " (NOT CACHED)");

							// The advert is not yet locally cached, 
							UnityWebRequest imageRequest = WebRequestTexture(ConvertToSecureProtocol(curAdData.imgUrl));

							DownloadHandlerTexture imageRequestDownloadHandler = (DownloadHandlerTexture) imageRequest.downloadHandler;

							// Wait for the request to complete
							yield return imageRequest.SendWebRequest();

							// Need to re-grab curAdData just incase it has been overwritten
							curAdData = GetAdDataByChar(adSize, slotChar);

							// Check for any errors
							if (!string.IsNullOrEmpty(imageRequest.error)) {
								Debug.LogError("IAS image download error - " + imageRequest.error);
								continue;
							}
							
							Texture2D imageTexture = imageRequestDownloadHandler.texture;

#if UNITY_EDITOR
							imageTexture.name = (int)adSize + slotChar.ToString() + " - " + ConvertToSecureProtocol(activeJsonURL);
#endif
							
							advertTextures.Add(new IASTextureData(imageTexture));
							adTextureInsertId++;
							curAdTextureInsertId = adTextureInsertId;

							yield return null;
							
							try {
								// Make sure the newly downloaded texture is POT sized
								if (!Mathf.IsPowerOfTwo(imageTexture.width) || !Mathf.IsPowerOfTwo(imageTexture.height)) {
									// Only resize static images, APNGs would just turn static if we used the returned byte data from this
									if (!curAdData.hasAnimatedImage || !enableAnimatedAdSupport) {
										// Disabled: No longer resizing and compressing legacy images as it affected quality too much to be usable
										// Static image not already POT on the serverside, resize it now
										//TextureScale.Scale(imageTexture, Mathf.ClosestPowerOfTwo(imageTexture.width / 2), Mathf.ClosestPowerOfTwo(imageTexture.height / 2));

										// Write the scaled initial image to disk
										//File.WriteAllBytes(filePath + fileName, imageTexture.EncodeToPNG());
										
										//if(compressLoadedTextures)
										//	imageTexture.Compress(true);
										
										// Write the initial file downloaded from the IAS server to disk
										File.WriteAllBytes(filePath + fileName, imageRequestDownloadHandler.data);
									} else {
										// We only get here if the animated image is not POT size, but this isn't support on the IAS server anyway
									
										// Write the initial file downloaded from the IAS server to disk
										File.WriteAllBytes(filePath + fileName, imageRequestDownloadHandler.data);
									
										// Can't compress this animated image because the size is not POT
									}
								} else {
									// Write the initial file downloaded from the IAS server to disk
									File.WriteAllBytes(filePath + fileName, imageRequestDownloadHandler.data);

									if (compressLoadedTextures)
										imageTexture.Compress(true);
								}

								if (curAdData.hasAnimatedImage) {
									// Force delete the animated frames cache as they'll need to be regenerated for the newly downloaded image
									DeleteAnimationFrames(Path.GetFileNameWithoutExtension(fileName) + "_frame_*");
								}

								curAdData.isTextureFileCached = true;
							} catch (IOException e) {
								Debug.LogError("IAS failed to create cache file - " + e.Message);
								curAdData.isTextureFileCached = false;
							} catch (UnauthorizedAccessException e) {
								Debug.LogError("IAS failed to create cache file - " + e.Message);
								curAdData.isTextureFileCached = false;
							}

							// Dispose of the imageRequest data as soon as we no longer need it (clear it from memory)
							imageRequestDownloadHandler.Dispose();
							imageRequest.Dispose();
						}

						curAdData.adTextureId = curAdTextureInsertId; //advertTextures.Count - 1;
						curAdData.isTextureReady = true;
						curAdData.isDownloading = false;
					} else {
						if (advancedLogging)
							Debug.Log("This advert is for the self app, no need to download it");
					}
				} else {
					if(advancedLogging)
						Debug.Log("Ad texture already ready and cache is up to date, no need to download anything new");
				}

				if (advancedLogging)
					Debug.Log("Finished loading ad for " + curAdData.packageName);

				if (OnIASImageDownloaded != null)
					OnIASImageDownloaded.Invoke(adSize);
			} else {
				Debug.LogError("Failed to get ad data for char " + slotChar);
			}

			// Wait a frame between each ad we preload
			yield return null;
		}

		SaveIASData();
	}

	public static UnityWebRequest WebRequestTexture(string url) {
		return UnityWebRequestTexture.GetTexture(url);
	}

	public static UnityWebRequest WebRequestString(string url) {
		return UnityWebRequest.Get(url);
	}

	private int GetUniqueUsableAdCount(IASAdSize adSize) {
		List<AdData> allSlotAds = advertData.slotInts[(int)adSize - 1].advert;
		HashSet<string> processedPackageNames = new HashSet<string>();

		for (int i = 0; i < allSlotAds.Count; i++) {
			if (!allSlotAds[i].isSelf && allSlotAds[i].isActive && !processedPackageNames.Contains(allSlotAds[i].packageName)) {
				processedPackageNames.Add(allSlotAds[i].packageName);
			}
		}

		return processedPackageNames.Count;
	}

	private char GetSlotChar(IASAdSize adSize, int offset = 0, AdJsonFileData customData = null) {
		AdSlotData curSlotData = GetAdSlotData(adSize, customData);
		
		if (curSlotData != null) {
			int maxAdOffset = GetMaxAdOffset(adSize);
			bool allowSelfAdvertising = maxAdOffset >= curSlotData.advert.Count;
			
			int totalAds = curSlotData.advert.Count;
			int lastSlotId = offset == 0 ? curSlotData.lastSlotId + 1 : curSlotData.lastBackscreenId;
			
			if (customData == null) {
				// Make sure all ads within preloadPackageNames are unique, otherwise fallback to showing ads already installed then fallback to allowing duplicates
				Dictionary<char, string> displayableAdPackageNames = new Dictionary<char, string>();
				
				// Pass 1: Make a list of unique games which are not self and not already installed
				for (int i = 0; i <= totalAds; i++) {
					int slotCharIdCheck = Mathf.Abs(lastSlotId + i) % totalAds;
					char slotCharCheck = (char)(slotCharIdCheck + slotIdDecimalOffset);
					
					AdData curAd = GetAdDataByChar(adSize, slotCharCheck);

					if ((allowSelfAdvertising || !curAd.isSelf) && curAd.isActive && !curAd.isInstalled && !displayableAdPackageNames.ContainsKey(curAd.slotChar) && !displayableAdPackageNames.ContainsValue(curAd.packageName)) {
						displayableAdPackageNames.Add(curAd.slotChar, curAd.packageName);
						// D, E
					}
				}

				// Based on number of available package names, determine if we should include already installed games on the IAS display
				bool includeInstalledGames = offset > 0 ? (adSize == IASAdSize.Square ? displayableAdPackageNames.Count < (squareBackscreenAds + 1) : displayableAdPackageNames.Count < (tallBackscreenAds + 1)) : displayableAdPackageNames.Count < 1;

				// Pass 2: if include installed games is true then add already installed games to the list of displayable ads
				if (includeInstalledGames) {
					displayableAdPackageNames.Clear();
					
					for (int i = 0; i <= totalAds; i++) {
						int slotCharIdCheck = Mathf.Abs(lastSlotId + i) % totalAds;
						char slotCharCheck = (char)(slotCharIdCheck + slotIdDecimalOffset);
					
						AdData curAd = GetAdDataByChar(adSize, slotCharCheck);

						if ((allowSelfAdvertising || !curAd.isSelf) && curAd.isActive && !displayableAdPackageNames.ContainsKey(curAd.slotChar) && !displayableAdPackageNames.ContainsValue(curAd.packageName)) {
							displayableAdPackageNames.Add(curAd.slotChar, curAd.packageName);
							// E, A, B, C, D
						}
					}
				}
				
				if(displayableAdPackageNames.Count > offset)
					return displayableAdPackageNames.ElementAt(offset).Key;
				
				return default(char);
			}

			int wantedSlotCharId = (lastSlotId + offset) % totalAds; // If we use modulo here then empty ad slots would have ads but it would mean duplicate ads could appear on the backscreen together
			
			//if(advancedLogging)
			//	Debug.Log("WantedSlotCharId: " + wantedSlotCharId);

			// Remove this to allow duplicate ads to show on the backscreen when there's not enough ads in the slot to fill it
			if (GetUniqueUsableAdCount(adSize) < offset && wantedSlotCharId - (offset - 1) < 0)
				return default(char);
			
			char wantedSlotChar = (char) (wantedSlotCharId + slotIdDecimalOffset);

			return wantedSlotChar;
		}
		
		return default(char);
	}

	public void ForceStopIASActions() {
		StopAllCoroutines();
		
		// Stop any animated texture load routines if any active
		if(activeAnimatedTextureLoadRoutine != null)
			StopCoroutine(activeAnimatedTextureLoadRoutine);

		animatedTextureLoadQueue.Clear();
	}

	public void ReDownloadIASData() {
		SaveIASData(true);
		
		if (OnIASForceReset != null)
			OnIASForceReset.Invoke();

		ForceStopIASActions();
		
		// Cleanup the downloaded IAS textures from memory
		foreach (IASTextureData advertTexture in advertTextures) {
			Destroy(advertTexture.staticTexture);
			
			foreach(IASTextureData.IASAnimatedFrameData animatedTextureFrames in advertTexture.animatedTextureFrames)
				Destroy(animatedTextureFrames.texture);
		}
		
		advertTextures.Clear();
		adTextureInsertId = -1;

		StartCoroutine(DownloadIASData(LoadIASData()));
	}

	private bool hasStartedIASDataDownloadThisSession = false;
	
	private IEnumerator DownloadIASData(bool cachedDataLoaded = false) {
		// Fallback to using cached data if there's no internet connection
		if (Application.internetReachability == NetworkReachability.NotReachable) {
			SaveIASData();

			if (advancedLogging)
				Debug.Log("IAS Done (Cached mode)");

			if(OnIASDataReady != null)
				OnIASDataReady.Invoke();
		
			// Do this after updating the advertData so were working with live values
			RefreshActiveAdSlots();
			
			yield break;
		}

		hasStartedIASDataDownloadThisSession = true;

		AdJsonFileData loadedAdData = null;
		
		if (cachedDataLoaded) {
			// The existing ad data in save data (if exists)
			loadedAdData = DeepCopyAsJsonFileData(advertData);
		}

		if (advancedLogging)
			Debug.Log("IAS downloading data..");

		// Download the JSON file
		UnityWebRequest jsonRequest = WebRequestString(ConvertToSecureProtocol(activeJsonURL));

		DownloadHandler jsonRequestDownloadHandler = jsonRequest.downloadHandler;

		// Wait for the request to complete
		yield return jsonRequest.SendWebRequest();

		// Check for any errors
		if (!string.IsNullOrEmpty(jsonRequest.error)) {
			FirebaseAnalyticsManager.LogError("IAS JSON download error - " + jsonRequest.error);

			if (advancedLogging)
				Debug.LogError("JSON download error! " + jsonRequest.error);

			yield break;
		} else if (jsonRequestDownloadHandler.text.Contains("There was an error")) {
			FirebaseAnalyticsManager.LogError("IAS JSON download error! Serverside system error!");

			if (advancedLogging)
				Debug.LogError("JSON download error! Serverside system error!");

			yield break;
		} else if (string.IsNullOrEmpty(jsonRequestDownloadHandler.text)) {
			FirebaseAnalyticsManager.LogError("IAS JSON download error! Empty JSON!");

			if (advancedLogging)
				Debug.LogError("JSON download error! Empty JSON!");

			yield break;
		}

		JsonFileData tempAdvertData = new JsonFileData();

		try {
			tempAdvertData = JsonUtility.FromJson<JsonFileData>(jsonRequestDownloadHandler.text);
		} catch (ArgumentException e) {
			FirebaseAnalyticsManager.LogError("IAS JSON data invalid - " + e.Message);

			if (advancedLogging)
				Debug.LogError("JSON data invalid!" + e.Message);

			yield break;
		}

		// Dispose of the json request data (clear it from memory)
		jsonRequestDownloadHandler.Dispose();
		jsonRequest.Dispose();

		if (tempAdvertData == null) {
			FirebaseAnalyticsManager.LogError("IAS temp advert data null!");

			if (advancedLogging)
				Debug.LogError("Temp advert data was null!");

			yield break;
		}

		if (tempAdvertData.slots.Count <= 0) {
			FirebaseAnalyticsManager.LogError("IAS temp advert data no slots!");

			if (advancedLogging)
				Debug.LogError("Temp advert data has no slots!");

			yield break;
		}

		bool needToRandomizeSlot = false;
		
		advertData.slotInts.Clear();
		
		// Create the IAS slots for each type of defined ad size (int array starts at 1 on backend server)
		for(int i=0; i < Enum.GetValues(typeof(IASAdSize)).Length;i++)
			advertData.slotInts.Add(new AdSlotData(i + 1, new List<AdData>()));

		// We're currently only using the slots, not containers
		for (int i = 0; i < tempAdvertData.slots.Count; i++) {
			try {
				JsonSlotData curSlot = tempAdvertData.slots[i];

				// We'll be converting the slot id (e.g 1a, 1c or 2f) into just number and just character values
				StringBuilder slotIntBuilder = new StringBuilder();
				StringBuilder slotCharBuilder = new StringBuilder();

				// Iterate through each chaacter in the slotid appending digit/letters to the appropriate string builder
				foreach (char c in curSlot.slotid) {
					if (Char.IsDigit(c)) {
						slotIntBuilder.Append(c);
					} else if (Char.IsLetter(c)) {
						slotCharBuilder.Append(c);
					}
				}
				
				// Parse the string builder digits to an int
				if (!Int32.TryParse(slotIntBuilder.ToString(), out int slotInt)) {
					if (advancedLogging)
						Debug.LogError("Failed to parse slot int from '" + curSlot.slotid + "'");

					yield break;
				}

				// Parse the string builder letters to a char
				if (!Char.TryParse(slotCharBuilder.ToString(), out char slotChar)) {
					if (advancedLogging)
						Debug.LogError("Failed to parse slot char from '" + curSlot.slotid + "'");

					yield break;
				}

				// If this slot doesn't exist yet create a new slot for it
				//if(!DoesSlotIntExist(slotInt, newAdvertData))
				//	newAdvertData.slotInts.Add(new AdSlotData(slotInt, new List<AdData>()));

				// Get the index in the list for slotInt
				int slotDataIndex = slotInt - 1; //GetSlotIndex(slotInt, newAdvertData);

				if (slotDataIndex < 0) {
					if (advancedLogging)
						Debug.LogError("Failed to get slotDataIndex!");

					yield break;
				}

				// Make sure this slot char isn't repeated in the json file within this slot int for some reason
				if (!DoesSlotCharExist((IASAdSize)slotInt, slotChar, advertData)) {
					advertData.slotInts[slotDataIndex].advert.Add(new AdData(slotChar));
				}

				/*if (advertData.slotInts.Count >= (slotDataIndex + 1)) {
					advertData.slotInts[slotDataIndex].lastSlotId = advertData.slotInts[slotDataIndex].lastSlotId;
				} else {
					needToRandomizeSlot = true;
				}*/

				int slotAdIndex = GetAdIndex((IASAdSize)slotInt, slotChar, advertData);

				if (slotAdIndex < 0) {
					if (advancedLogging)
						Debug.LogError("Failed to get slotAdIndex! Could not find " + slotInt + ", " + slotChar.ToString());

					yield break;
				}

				// The new ad data we're creating right now
				AdData curAdData = advertData.slotInts[slotDataIndex].advert[slotAdIndex];
				string packageName = "";

				// Extract the bundleId of the advert
#if UNITY_ANDROID
				AppStore store = CrossPlatformManager.GetActiveStore();

				if (store == AppStore.GooglePlay) {
					// Regex extracts the id GET request from the URL which is the package name of the game
					// (replaces everything that does NOT match id=blahblah END or NOT match id=blahblah AMERPERSAND
					packageName = Regex.Match(curSlot.adurl, "(?<=id=)((?!(&|\\?)).)*").Value;
				} else {
					// For other platforms we should be fine to just use the full URL for package name comparisons as we'll be using .Compare
					// And other platforms won't include any other referral bundle ids in their URLs
					packageName = curSlot.adurl;
				}
#elif UNITY_IOS
					// IOS we just need to grab the name after the hash in the URL
					packageName = Regex.Match(curSlot.adurl, "(?<=.*#).*").Value;
#else
					// For other platforms we should be fine to just use the full URL for package name comparisons as we'll be using .Compare
					// And other platforms won't include any other referral bundle ids in their URLs
					packageName = curSlot.adurl;
#endif

				// If an animated image is set then use that instead of the static image
				curAdData.hasAnimatedImage = enableAnimatedAdSupport && !string.IsNullOrEmpty(curSlot.animated_imgurl);

				curAdData.imgUrl = ConvertToSecureProtocol(curAdData.hasAnimatedImage ? curSlot.animated_imgurl : curSlot.imgurl);

				string imageFileType = Regex.Match(curAdData.imgUrl, "(?<=/uploads/adverts/.*)\\.[A-z]*[^(\\?|\")]").Value;

				curAdData.adUniqueId = curSlot.adid;
				curAdData.fileName = curSlot.slotid + (curAdData.hasAnimatedImage ? "_animated" : "") + imageFileType;
				curAdData.isSelf = packageName.Contains(bundleId);
				curAdData.isActive = curSlot.active;
				curAdData.isInstalled = IsPackageInstalled(packageName);
				curAdData.adUrl = curSlot.adurl;
				curAdData.packageName = packageName;

				if (cachedDataLoaded) {
					if (loadedAdData.slotInts.Count > slotDataIndex && loadedAdData.slotInts[slotDataIndex].advert.Count > slotAdIndex) {
						AdData curLoadedAdData = loadedAdData.slotInts[slotDataIndex].advert[slotAdIndex];
						
						if (advancedLogging)
							Debug.Log("Cache info for " + curAdData.fileName + ": Timestamp of cached ad: " + curLoadedAdData.lastUpdated + " / Timestamp of latest on server: " + curSlot.updatetime);

						// Check if the cached active data needs the ad textures reloading
						// curSlot.updatetime = timestamp the ad was last updated on ias server
						// curAdData.lastUpdated = timestamp we last updated that ad slot in the app
						if (curSlot.updatetime > curLoadedAdData.lastUpdated || curLoadedAdData.lastUpdated == 0L) {
							if (advancedLogging)
								Debug.Log("Slot " + curSlot.slotid + "has changed! We will download new IAS ad data..");
							curAdData.isTextureFileCached = false;
						} else {
							curAdData.isTextureFileCached = curLoadedAdData.isTextureFileCached;
						}

						if (advertData.slotInts.Count >= (slotDataIndex + 1)) {
							advertData.slotInts[slotDataIndex].lastSlotId = loadedAdData.slotInts[slotDataIndex].lastSlotId;
							advertData.slotInts[slotDataIndex].lastBackscreenId = loadedAdData.slotInts[slotDataIndex].lastBackscreenId;
						}
					} else {
						needToRandomizeSlot = true;
					
						if(advancedLogging)
							Debug.Log("Slot " + curSlot.slotid + " does not yet have cached data stored..");
					}
				} else {
					needToRandomizeSlot = true;
					
					if(advancedLogging)
						Debug.Log("Slot " + curSlot.slotid + " does not yet have cached data stored..");
				}
				
				curAdData.newUpdateTime = curSlot.updatetime;
			} catch (ArgumentNullException e) {
				if (advancedLogging)
					Debug.LogError("Missing slot parameter! " + e.Message);

				continue;
			}
		}
		
		if (needToRandomizeSlot) {
			if(advancedLogging)
				Debug.Log("First time launch, randomising IAS offset");
			
			RandomizeAdSlots(advertData);
		} else {
			if(advancedLogging)
				Debug.Log("Repeated launch, continuing from last IAS offset");
		}

		CleanupOldIASTextures();

		yield return null;
		
		SaveIASData();

		if (advancedLogging)
			Debug.Log("IAS Done");

		if(OnIASDataReady != null)
			OnIASDataReady.Invoke();
		
		// Do this after updating the advertData so were working with live values
		RefreshActiveAdSlots();
	}

	// Deletes IAS textures which are not in the loaded/downloaded JSON data
	private void CleanupOldIASTextures() {
		// Get a list of all IAS files in the data directory
		string[] iasFiles = Directory.GetFiles(Application.persistentDataPath, "IAS_*");

		foreach (string filePath in iasFiles) {
			bool fileUsed = false;
			string fileName = Path.GetFileName(filePath);
			bool animatedFrame = fileName.Contains("_frame");

			// Iterate through each ad size
			foreach (AdSlotData adSlotData in advertData.slotInts) {
				// Iterate through each ad
				foreach (AdData adData in adSlotData.advert) {
					string adFileName = "IAS_" + adData.fileName;
					
					if (!animatedFrame) {
						if (fileName == adFileName) {
							fileUsed = true;
							break;
						}
					} else {
						string adDataFileNameNoExt = Path.GetFileNameWithoutExtension(adFileName);

						if (adData.hasAnimatedImage && fileName.Contains(adDataFileNameNoExt)) {
							// This is a frame of a valid ad with animation frames (all animation frames get deleted when downloading new frames too)
							fileUsed = true;
							break;
						}
					}
				}
			}
			
			// Delete the IAS file if it's no longer being used by the IAS
			if (!fileUsed) {
				if(advancedLogging)
					Debug.Log("Deleting unused IAS file: " + filePath);
				
				File.Delete(filePath);
			}
		}
	}

	// Save the IAS data as the user quit the app (as saving whenever the data is updated is expensive)
	// OnApplicationQuit isn't always called as the player may just minimize then kill the app when
	// Or on iOS the app is suspended (calling OnApplicationPause(true)) unless "Exit on suspend" is enabled
	void OnApplicationQuit() {
		if (!storeSupportsIAS)
			return;

		if (hasQuitForceSaveBeenCalled)
			return;

		SaveIASData(true);
		hasQuitForceSaveBeenCalled = true;
	}

	void OnApplicationPause(bool pauseState) {
		if (!storeSupportsIAS)
			return;

		if (pauseState) {
			if (hasQuitForceSaveBeenCalled)
				return;

			//SaveIASData(true);
			hasQuitForceSaveBeenCalled = true;
		} else {
			hasQuitForceSaveBeenCalled = false;
		}
	}

	private Dictionary<string, int> failedPingURLs = new Dictionary<string, int>();
	private int activePings = 0;
	
	public void PingURL(string url) {
		StartCoroutine(DoPingURL(url));
	}

	private IEnumerator DoPingURL(string url) {
		activePings++;
		
		UnityWebRequest request = UnityWebRequest.Get(url);
		yield return request.SendWebRequest();

		if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200) {
			activePings--;
			
			if(advancedLogging)
				Debug.Log("Successfully pinged: " + url);
		} else {
			activePings--;
			
			if(advancedLogging)
				Debug.Log("Ping failed, it will retry soon: " + url);
			
			if (failedPingURLs.ContainsKey(url)) {
				failedPingURLs[url]++;
			} else {
				failedPingURLs.Add(url, 1);
			}
		}
	}

	private void RetryFailedPings() {
		foreach (string url in failedPingURLs.Keys) {
			for (int i = 0; i < failedPingURLs[url]; i++) {
				StartCoroutine(DoRetryPingURL(url));
			}
		}
	}

	private IEnumerator DoRetryPingURL(string url) {
		activePings++;
		
		UnityWebRequest request = UnityWebRequest.Get(url);
		yield return request.SendWebRequest();

		if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200) {
			activePings--;
			
			if(advancedLogging)
				Debug.Log("Successful response retrying URL ping - " + url);

			if (failedPingURLs.ContainsKey(url)) {
				failedPingURLs[url]--;

				if (failedPingURLs[url] <= 0)
					failedPingURLs.Remove(url);
			}
		} else {
			activePings--;
			
			if(advancedLogging)
				Debug.LogError("Invalid response retrying URL ping - " + url + " response code: " + request.responseCode);
		}
	}

	// Opens a target URL but first waits up to 2 seconds for active pings to finish
	public void OpenURL(string url) {
		StartCoroutine(DoOpenURL(url));
	}

	private IEnumerator DoOpenURL(string url) {
		// Wait a frame for any pings to begin
		yield return null;

		float waitTime = 0f;
		
		// Wait until either all active pings have finished or 2 seconds have passed since URL open was requested
		while (activePings > 0 && waitTime < 2f) {
			waitTime += Time.unscaledDeltaTime;
			
			yield return null;
		}

		Application.OpenURL(url);
	}

	public void OnImpression(int adId) {
		if (!storeSupportsIAS || adId < 0)
			return;

		// Only log an impression per adId once per session so we're not biased by games spam showing a ton of IAS ads
		if (!loggedImpressions.Contains(adId)) {
			loggedImpressions.Add(adId);
			PingURL("https://ias.gamepicklestudios.com/track/" + activeSlotId + "/" + adId);
			
			if(advancedLogging)
				Debug.Log("Logging IAS impression to: https://ias.gamepicklestudios.com/track/" + activeSlotId + "/" + adId);
		}
	}

	public void OnClick(int adId) {
		if (!storeSupportsIAS || adId < 0)
			return;

		FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.IAS_CLICK);
		
		// Only log a click per adId once per session so stats aren't inflated by a user spam clicking an ad multiple times
		if (!loggedClicks.Contains(adId)) {
			loggedClicks.Add(adId);
			PingURL("https://ias.gamepicklestudios.com/click/" + activeSlotId + "/" + adId);
			
			if(advancedLogging)
				Debug.Log("Logging IAS click to: https://ias.gamepicklestudios.com/click/" + activeSlotId + "/" + adId);
		}
	}

	/// <summary>
	/// Refreshes the IAS adverts
	/// </summary>
	/// <param name="adSize">Slot int</param>
	public static void RefreshBanners(IASAdSize adSize, bool forceChangeActive = false, AdJsonFileData customData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return;

		if (!instance.DoesSlotIntExist(adSize, customData)) {
			if (!instance.hasStartedIASDataDownloadThisSession && Application.internetReachability != NetworkReachability.NotReachable) {
				Debug.Log("Internet reachable, retrying IAS data download..");
				
				instance.ReDownloadIASData();
			} else {
#if UNITY_EDITOR
				Debug.Log("(Editor Only) Attempted to refresh a banner slot which was either blacklisted or not yet ready! (Slot " + (int) adSize + ") This will do nothing");
#endif

				if (instance.advancedLogging)
					Debug.Log("Refresh failed, blacklisted? Slot " + (int) adSize);
			}

			return;
		}

		if (instance.advancedLogging)
			Debug.Log("Refreshing banners for adSize: " + (int) adSize + " has custom data? " + (customData != null ? "YES" : "NO"));

		instance.IncSlotChar(adSize, customData);

		if (forceChangeActive) {
			if (OnForceChangeWanted != null) {
				OnForceChangeWanted.Invoke(adSize);
			}
		}
	}

	/// <summary>
	/// Returns whether the ad texture has downloaded or not
	/// </summary>
	/// <returns><c>true</c> if is ad ready the specified wantedSlotInt; otherwise, <c>false</c>.</returns>
	/// <param name="wantedSlotInt">Slot int</param>
	public static bool IsAdReady(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return false;

		AdData returnValue = instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return returnValue.isTextureReady;
		
		return false;
	}

	public static int GetAdId(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return -1;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return returnValue.adUniqueId;

		return -1;
	}
	
	public static int GetAdTextureId(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return -1;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return returnValue.adTextureId;

		return -1;
	}
	
	public static string GetAdFilename(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return string.Empty;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return "IAS_" + returnValue.fileName;

		return string.Empty;
	}
	
	/// <summary>
	/// Returns the URL of the current active advert from the requested JSON file and slot int
	/// </summary>
	/// <returns>The advert URL</returns>
	/// <param name="wantedSlotInt">Slot int</param>
	public static string GetAdURL(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return string.Empty;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return returnValue.adUrl;

		return string.Empty;
	}

	/// <summary>
	/// Returns the package name of the current active advert from the requested JSON file and slot int
	/// </summary>
	/// <returns>The advert package name</returns>
	/// <param name="wantedSlotInt">Slot int</param>
	public static string GetAdPackageName(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return string.Empty;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return returnValue.packageName;

		return string.Empty;
	}

	/// <summary>
	/// Returns the Texture of the current active advert from the requested JSON file and slot int
	/// </summary>
	/// <returns>The advert texture</returns>
	/// <param name="jsonFileId">JSON file ID</param>
	/// <param name="wantedSlotInt">Slot int</param>
	public static Texture2D GetAdTexture(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return null;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return instance.advertTextures[returnValue.adTextureId].staticTexture;
		
		return null;
	}

	public static List<IASTextureData.IASAnimatedFrameData> GetAdTextureFrames(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.storeSupportsIAS)
			return null;

		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (returnValue != null)
			return instance.advertTextures[returnValue.adTextureId].animatedTextureFrames;

		return null;
	}

	public static bool IsAnimatedAdTexture(IASAdSize adSize, int offset = 0, AdData adData = null) {
		if (!instance || !instance.enableAnimatedAdSupport)
			return false;
		
		AdData returnValue = adData ?? instance.GetAdData(adSize, offset);

		if (adData != null)
			return returnValue.hasAnimatedImage;

		return false;
	}

}