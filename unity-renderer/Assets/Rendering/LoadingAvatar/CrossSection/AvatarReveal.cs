using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class AvatarReveal : MonoBehaviour
{
    public bool avatarLoaded;
    public GameObject revealer;
    public Transform finalPosition;
    public ParticleSystem particles;
    public ParticleSystem revealParticles;

    public float revealSpeed;

    public float fadeInSpeed;
    public Renderer ghostDummy;
    Material _ghostMaterial;
    public List<Renderer> targets = new List<Renderer>();
    List<Material> materials = new List<Material>();

    private void Start()
    {
        _ghostMaterial = ghostDummy.material;

        foreach (Renderer r in targets)
        {
            materials.Add(r.material);
        }
    }
    private void Update()
    {
        UpdateMaterials();

        if(avatarLoaded)
        {
            RevealAvatar();
        }
    }

    void RevealAvatar()
    {
        if (revealer.transform.position.y < finalPosition.position.y)
        {
            revealer.transform.position += revealer.transform.up * revealSpeed * Time.deltaTime;

            if (!particles.isPlaying)
            {
                particles.Play();
                revealParticles.Play();
            }
        }
        else if (particles.isPlaying)
        {
            particles.Stop();
            revealParticles.Stop();
        }
    }

    void UpdateMaterials()
    {
        if(_ghostMaterial.GetColor("_Color").a < 0.9f)
        {
            Color gColor = _ghostMaterial.GetColor("_Color");
            Color tempColor = new Color(gColor.r, gColor.g, gColor.b, gColor.a + Time.deltaTime * fadeInSpeed);
            _ghostMaterial.SetColor("_Color", tempColor);
        }        

        _ghostMaterial.SetVector("_RevealPosition", revealer.transform.position);

        foreach (Material m in materials)
        {
            m.SetVector("_RevealPosition", revealer.transform.position);
        }
    }
}