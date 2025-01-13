using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Mod
{
    //Hi
    public class Mod : MonoBehaviour
    {
        public static Keys keys;
        public static Sprite sprite;

        public static void Main()
        {
            if (sprite == null)
                sprite = ModAPI.LoadSprite("default.png");

            ModAPI.Register(
                new Modification()
                {
                    OriginalItem = ModAPI.FindSpawnable("Metal Cube"),
                    NameOverride = $"Reference image [REF]",
                    DescriptionOverride = "Press F7 to make reference ignore the cursor\n" +
					"Press F8 to make the reference start responding to the cursor again\nThese keys can be changed in \"ppgfolder\\Mods\\referenceModKeys.json\"",
                    CategoryOverride = ModAPI.FindCategory("Misc."),
                    ThumbnailOverride = sprite,
                    AfterSpawn = (Instance) =>
                    {
                        Instance.GetComponent<SpriteRenderer>().sprite = sprite;

						if (Instance.HasComponent<OpacitySaver>() == false)
						{
							Instance.AddComponent<OpacitySaver>();
						}

						if (Instance.HasComponent<Reference>() == false)
                        {
                            Instance.AddComponent<Reference>();
                        }

                        Instance.FixColliders();
                    }
                }
            );

            if (keys == null)
            {
                keys = new Keys();

                try
                {
                    keys = ModAPI.DeserialiseJSON<Keys>("referenceModKeys.json");
                }
                catch
                {
                    ModAPI.SerialiseJSON(keys, "referenceModKeys.json");
                }
            }
        }
    }

    [Serializable]
    public class Keys
    {
        public KeyCode startIgnoringCursor = KeyCode.F7;
        public KeyCode stopIgnoringCursor = KeyCode.F8;
    }

	//I didn't have the opacity variable saved in the base class for some reason
	//so i had to move it to a separate class and it seems to work
	public class OpacitySaver : MonoBehaviour
	{
		public float opacity = 1f;
	}

	public class Reference : MonoBehaviour
    {
		public string path;
        public bool enableRotation;
        public bool isUrl;

		private Quaternion rot;
		private DialogBox pathDialogBox;
		private DialogBox urlDialogBox;
		private DialogBox sizeDialogBox;
		private DialogBox opacityDialogBox;
		private SpriteRenderer spriteRenderer;
		private PhysicalBehaviour pb;
		private OpacitySaver opacitySaver;

        private Sprite urlSprite;
        private UrlResult urlResult;

        private void Start()
        {
			opacitySaver = GetComponent<OpacitySaver>();
			rot = transform.rotation;

			spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sortingOrder = -999;
            spriteRenderer.sortingLayerName = "Default";
            spriteRenderer.material = ModAPI.FindMaterial("Sprites-Default");

			Color c = spriteRenderer.color;
			c.a = opacitySaver.opacity;
			spriteRenderer.color = c;

			pb = GetComponent<PhysicalBehaviour>();
            pb.SimulateTemperature = false;
            gameObject.SetLayer(10);

            RegisterContextButtons();

            ModAPI.Notify(isUrl);

            if (string.IsNullOrEmpty(path) == false)
            {
                if (isUrl == false)
                    UpdateImage(path);
                else
                    StartCoroutine(StartDownloadURL(path));
            }
        }

        private void RegisterContextButtons()
        {
            ContextMenuOptionComponent context = GetComponent<PhysicalBehaviour>().ContextMenuOptions;

            context.Buttons.Add(new ContextMenuButton("togglerotation[ref]", "Toggle rotation", "Toggle rotation", () =>
            {
                rot = transform.rotation;
                enableRotation = !enableRotation;
            }));

            PathDialogBox(context);
            UrlDialogBox(context);
			OpacityDialogBox(context);
            SizeDialogBox(context);
        }

		private void OpacityDialogBox(ContextMenuOptionComponent context)
		{
			DialogButton apply = new DialogButton("Apply", true, (UnityAction)(() =>
			{
				float percentage;

				if (float.TryParse(opacityDialogBox.EnteredText, NumberStyles.Any, CultureInfo.InvariantCulture, out percentage))
				{
					opacitySaver.opacity = Mathf.Clamp(percentage / 100f, 0, 1);

					Color c = spriteRenderer.color;
					c.a = opacitySaver.opacity;
					spriteRenderer.color = c;

					ModAPI.Notify("Opacity changed");
				}
				else
				{
					ModAPI.Notify("Wrong format");
				}
			}));

			DialogButton cancel = new DialogButton("Cancel", true, (UnityAction)(() =>
			{
				opacityDialogBox.Close();
			}));

			context.Buttons.Add(new ContextMenuButton("changeopacity[ref]", "Change opacity", "Change opacity", () =>
			{
				opacityDialogBox = DialogBoxManager.TextEntry("Enter the opacity percentage (0% - 100%)", "Opacity", apply, cancel);
				opacityDialogBox.EnteredText = (spriteRenderer.color.a * 100).ToString();
			}));
		}

        private void PathDialogBox(ContextMenuOptionComponent context)
        {
            DialogButton apply = new DialogButton("Apply", true, (UnityAction)(() =>
			{
                string path = pathDialogBox.EnteredText.Replace("\"", string.Empty);

                if (UpdateImage(path))
                {
                    isUrl = false;
                    ModAPI.Notify("Successfully");
                }
                else
                    ModAPI.Notify("Unsuccessful");
            }));

            DialogButton cancel = new DialogButton("Cancel", true, (UnityAction)(() =>
            {
                pathDialogBox.Close();
            }));

            context.Buttons.Add(new ContextMenuButton("setpath[ref]", "Change image", "Change image", () =>
            {
                pathDialogBox = DialogBoxManager.TextEntry("Enter the full path to the file on disk", "Full Path", apply, cancel);
            }));
        }

        private void UrlDialogBox(ContextMenuOptionComponent context)
        {
            DialogButton apply = new DialogButton("Apply", true, (UnityAction)(() =>
            {
                string path = urlDialogBox.EnteredText;

                StartCoroutine(StartDownloadURL(path));
            }));

            DialogButton cancel = new DialogButton("Cancel", true, (UnityAction)(() =>
            {
                urlDialogBox.Close();
            }));

            context.Buttons.Add(new ContextMenuButton("seturl[ref]", "Change image from URL", "Change image from URL", () =>
            {
                urlDialogBox = DialogBoxManager.TextEntry("Enter the URL", "URL", apply, cancel);
            }));
        }

        private void SizeDialogBox(ContextMenuOptionComponent context)
        {
            DialogButton apply = new DialogButton("Apply", true, (UnityAction)(() =>
            {
                string[] input = sizeDialogBox.EnteredText.Split(',');

                float x = 0;
                float y = 0;

                if (float.TryParse(input[0], NumberStyles.Any, CultureInfo.InvariantCulture, out x) && float.TryParse(input[1], NumberStyles.Any, CultureInfo.InvariantCulture, out y))
                {
                    transform.localScale = new Vector2(x, y);
                    ModAPI.Notify("Changed");
                }
                else
                {
                    ModAPI.Notify("Wrong format");
                }
            }));

            DialogButton cancel = new DialogButton("Cancel", true, (UnityAction)(() =>
            {
                sizeDialogBox.Close();
            }));

            context.Buttons.Add(new ContextMenuButton("setsize[ref]", "Change size", "Change size", () =>
            {
                sizeDialogBox = DialogBoxManager.TextEntry("Enter size (X.0, Y.0) Default size (1, 1)", "Size (X.0, Y.0)", apply, cancel);

                sizeDialogBox.EnteredText = $"{transform.localScale.x}, {transform.localScale.y}";
            }));
        }

        private bool UpdateImage(string path)
        {
            Sprite sprite = LoadSprite(path);

            if (sprite == null)
                return false;

            if (spriteRenderer.sprite != Mod.sprite)
            {
                UnityEngine.Object.Destroy(spriteRenderer.sprite.texture);
                UnityEngine.Object.Destroy(spriteRenderer.sprite);
            }

			spriteRenderer.sprite = sprite;
            gameObject.FixColliders();
            pb.RefreshOutline();
            this.path = path;
            ModAPI.Notify("Successfully");

            return true;
        }

        private void Update()
        {
            if (pb.IsWeightless == false)
                pb.MakeWeightless();

            if (enableRotation == false)
                transform.rotation = rot;

            if (Input.GetKeyDown(Mod.keys.startIgnoringCursor))
            {
                foreach (Collider2D c in GetComponents<Collider2D>())
                {
                    c.enabled = false;
                }

                ModAPI.Notify("Moving references is disabled");
            }
            else if (Input.GetKeyDown(Mod.keys.stopIgnoringCursor))
            {
                foreach (Collider2D c in GetComponents<Collider2D>())
                {
                    c.enabled = true;
                }

                ModAPI.Notify("Moving references is enabled");
            }

            if (urlSprite != null)
            {
                spriteRenderer.sprite = urlSprite;
                urlSprite = null;
                isUrl = true;

                gameObject.FixColliders();
                pb.RefreshOutline();
            }

            pb.rigidbody.velocity = Vector2.zero;
            pb.rigidbody.angularVelocity = 0;
        }

        private IEnumerator StartDownloadURL(string url)
        {
            yield return DownloadRawDataURL(url);
        }

        private async Task DownloadRawDataURL(string url)
        {
            byte[] data;

            try
            {
                data = await Utils.HttpDownload(url);
            }
            catch
            {
                urlResult = UrlResult.Failure;
                return;
            }

            try
            {
                string time = DateTime.Now.ToString("dd-MM-yyyy_HH-mm-ff");
                string temp = Path.Combine(Path.GetTempPath(), $"refmod__{time}.img");

                using (Stream stream = File.OpenWrite(temp))
                {
                    stream.Write(data, 0, data.Length);
                }

                urlSprite = LoadSprite(temp);

                File.Delete(temp);
            }
            catch (Exception e)
            {
                ModAPI.Notify(e);
                urlResult = UrlResult.Failure;
                return;
            }

            urlResult = UrlResult.Success;
            path = url;
        }

        private Sprite LoadSprite(string path, float scale = 1f, bool pixelated = true)
        {
            return Utils.LoadSprite(path, (!pixelated) ? FilterMode.Bilinear : FilterMode.Point, 32f, markNonReadable: false);
        }

        private enum UrlResult : byte
        {
            Success,
            Failure
        }
    }
}