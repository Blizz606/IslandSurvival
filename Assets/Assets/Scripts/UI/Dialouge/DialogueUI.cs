using UnityEngine;
using UnityEngine.InputSystem;

public class DialogueUI : MonoBehaviour
{
    [SerializeField] private GameObject hud;
    [SerializeField] private GameObject dialogBox;
    [SerializeField] private bool showOnStart = true;

    private void Start()
    {
        if (showOnStart)
            OpenDialogue();
        else
            CloseDialogue();
    }

    private void Update()
    {
        if (dialogBox.activeSelf &&
            Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            CloseDialogue();
        }
    }

    public void OpenDialogue()
    {
        hud.SetActive(false);
        dialogBox.SetActive(true);
    }

    public void CloseDialogue()
    {
        hud.SetActive(true);
        dialogBox.SetActive(false);
    }
}