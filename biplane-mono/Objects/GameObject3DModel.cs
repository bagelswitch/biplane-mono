using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Biplane.Service;
using Biplane.Objects.Static;
using System.Linq;

namespace Biplane.Objects
{
    public abstract class GameObject3DModel : GameObject
    {
        public Game game;

        public float scale = 1.0f;

        public string modelName;

        public bool useXEffects = false;

        public bool useFXEffects = false;

        public bool hasShadow = false;

        private static Dictionary<String, Effect> effectMap = new Dictionary<String, Effect>();

        private bool exploded = false;

        private static Random smokeRand = new Random();

        const int fireParticlesPerFrame = 3;

        public ParticleSystem.ParticleEmitter trailEmitter;

        public float trailParticlesPerSecond = 200;
        public int numExplosionParticles = 30;
        public int numExplosionSmokeParticles = 50;

        private static Dictionary<String, Matrix[]> transformStore = new Dictionary<string,Matrix[]>();
        private Matrix[] transforms;

        private static BasicEffect shadowEffect;
        private static Matrix shadowMatrix = Matrix.CreateShadow(Vector3.Up, new Plane(Vector3.Up, 0));
        private static Vector3 shadowColor = new Vector3(.2f, .2f, .2f);

        private static BasicEffect basic3dsEffect;

        private Matrix yprt = new Matrix();
        private Matrix transMat = new Matrix();
        private Matrix scaleMat = new Matrix();
        private Matrix posNegMat = new Matrix();
        private Matrix shadowAltMat = new Matrix();

        public Model model;

        private BlendState blendState;
        private DepthStencilState depthStencilState;

        private Matrix localWorld;

        private Matrix shadowWorld;

        private Matrix effectWorld;

        private Matrix extWorld;

        private Matrix shadowScale;

        private float timer;

        private GraphicsDevice gd;

        private Vector3 jitter;

        private static Vector3 specColor = new Vector3(0.6f, 0.4f, 0.2f);
        private static int specPower = 12;

        public GameObject3DModel(Game game)
        {
            this.game = game;
        }

        public new void setup()
        {
            base.setup();
        }

        public virtual void loadModel()
        {
            if (this.model == null)
            {
                this.model = game.Content.Load<Model>(this.modelName);
            }
        }

        private void getAbsoluteBoneTransforms()
        {
            if(!transformStore.ContainsKey(this.modelName)) {
                Matrix[] newtransforms = new Matrix[256];
                model.CopyAbsoluteBoneTransformsTo(newtransforms);
                transformStore.Add(this.modelName, newtransforms);
            }
            this.transforms = transformStore[this.modelName];
        }

        public void draw(Matrix view, Matrix projection)
        {
            loadModel();
            getAbsoluteBoneTransforms();

            Matrix.CreateScale(this.scale, out this.scaleMat);

            // Set suitable renderstates for drawing a 3D model.
            blendState = this.game.GraphicsDevice.BlendState;
            depthStencilState = this.game.GraphicsDevice.DepthStencilState;

            this.game.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            this.game.GraphicsDevice.BlendState = BlendState.AlphaBlend;

            Matrix.CreateFromQuaternion(ref modelOrientation, out yprt);
            Matrix.CreateTranslation(ref position, out transMat);
            localWorld = yprt * transMat;
            
            if (this.useXEffects)
            {
                drawModelFbxEffect(model, view, projection, localWorld);
            }
            else if (this.useFXEffects)
            {
                drawModelExtEffect(model, view, projection, localWorld);
            }
            else
            {
                drawModelBasic(model, view, projection, localWorld);
            }

            if (this.hasShadow)
            {
                drawShadow(model, view, projection, localWorld);
            }
        }

        private void drawModelBasic(Model model, Matrix view, Matrix projection, Matrix localWorld)
        {
            //Draw the model, a model can have multiple meshes, so loop
            foreach (ModelMesh mesh in model.Meshes)
            {
                //This is where the mesh orientation is set, as well as our camera and projection
                foreach (BasicEffect effect in mesh.Effects)
                {
                    effect.EnableDefaultLighting();
                    effect.SpecularColor = specColor;
                    effect.SpecularPower = specPower;

                    // Set the fog to match the black background color
                    effect.FogEnabled = true;
                    effect.FogColor = Vector3.One * 0.75f;
                    effect.FogStart = 1000000;
                    effect.FogEnd = 2000000;

                    //effect.World = transforms[mesh.ParentBone.Index] * localWorld;
                    effect.World = transforms[mesh.ParentBone.Index] * this.scaleMat * localWorld;
                    effect.View = view;
                    effect.Projection = projection;

                    effect.Alpha = 1.0f;
                }
                //Draw the mesh, will use the effects set above.
                game.GraphicsDevice.BlendState = BlendState.Opaque;
                game.GraphicsDevice.DepthStencilState = DepthStencilState.Default;

                mesh.Draw();
            }
        }

        private void drawShadow(Model model, Matrix view, Matrix projection, Matrix localWorld)
        {
            gd = game.GraphicsDevice;

            game.GraphicsDevice.BlendState = BlendState.Opaque;
            game.GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            if (shadowEffect == null)
            {
                shadowEffect = new BasicEffect(gd);
                shadowEffect.LightingEnabled = false;
                shadowEffect.Alpha = 1.0f;
                shadowEffect.DiffuseColor = shadowColor;
                shadowEffect.TextureEnabled = false;
            }

            shadowEffect.Techniques[0].Passes[0].Apply();

            //Draw the model, a model can have multiple meshes, so loop
            foreach (ModelMesh mesh in model.Meshes)
            {
                float shadowSize = this.scale / (1 + (Vector3.Distance(this.position, new Vector3(position.X, Terrain.GetHeight(position), position.Z))/ 10000.0f));
                shadowWorld = transforms[mesh.ParentBone.Index];
                Matrix.CreateScale(shadowSize, out shadowScale);
                shadowWorld *= shadowScale;
                shadowWorld *= localWorld;
                Vector3 posNeg = Vector3.UnitY * position.Y * -1.0f;
                Matrix.CreateTranslation(ref posNeg, out posNegMat);
                shadowWorld *= posNegMat;
                shadowWorld *= shadowMatrix;
                Vector3 shadowAlt = Vector3.Up * (Terrain.GetHeight(position) + 200.0f);
                Matrix.CreateTranslation(ref shadowAlt, out shadowAltMat);
                shadowWorld *= shadowAltMat;
                shadowEffect.World = shadowWorld;
                shadowEffect.View = view;
                shadowEffect.Projection = projection;

                shadowEffect.Techniques[0].Passes[0].Apply();

                foreach (ModelMeshPart meshPart in mesh.MeshParts)
                {
                    gd.SamplerStates[0] = SamplerState.LinearWrap;

                    if (meshPart.PrimitiveCount > 0)
                    {
                        // setup vertices and indices
                        gd.SetVertexBuffer(meshPart.VertexBuffer);
                        gd.Indices = meshPart.IndexBuffer;

                        gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, meshPart.StartIndex, meshPart.PrimitiveCount);
                    }
                }
            }
        }

        protected void drawModelFbxEffect(Model model, Matrix view, Matrix projection, Matrix localWorld)
        {
            gd = game.GraphicsDevice;
            gd.BlendState = BlendState.AlphaBlend;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.RasterizerState = RasterizerState.CullNone;

            foreach (ModelMesh mesh in model.Meshes)
            {
                foreach (ModelMeshPart part in mesh.MeshParts)
                {
                    var effect = part.Effect;

                    if (part.PrimitiveCount > 0)
                    {
                        BasicEffect basicEffect = effect as BasicEffect;

                        this.effectWorld = transforms[mesh.ParentBone.Index] * this.scaleMat * localWorld;

                        if (basicEffect != null)
                        {
                            basicEffect.World = effectWorld;
                            basicEffect.View = view;
                            basicEffect.Projection = projection;
                            //basicEffect.Alpha = 1;
                        }
                        else
                        {
                            effect.Parameters["WorldViewProjection"].SetValue(effectWorld * view * projection);
                        }

                        // setup vertices and indices
                        gd.SetVertexBuffer(part.VertexBuffer);
                        gd.Indices = part.IndexBuffer;

                        for(int i = 0; i < effect.CurrentTechnique.Passes.Count; i++)
                        {
                            effect.CurrentTechnique.Passes[i].Apply();
                            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, part.VertexOffset, part.StartIndex, part.PrimitiveCount);
                        }

                        
                    }
                }
            }
        }

        private void drawModelExtEffect(Model model, Matrix view, Matrix projection, Matrix localWorld)
        {
            // Draw the model.
            foreach (ModelMesh mesh in model.Meshes)
            {
                String effectName = null;
                Char[] sep = new char[1];
                sep[0] = '-';
                String[] meshNameList = mesh.Name.Split(sep);
                if (meshNameList.Length > 2)
                {
                    effectName = meshNameList[1];
                    Effect effect = null;
                    if (!effectMap.ContainsKey(effectName))
                    {
                        effect = game.Content.Load<Effect>("Effects\\" + effectName);
                        effectMap.Add(effectName, effect);

                        if (effect is BasicEffect)
                        {
                            BasicEffect beffect = (BasicEffect)effect;

                            beffect.EnableDefaultLighting();
                            beffect.SpecularColor = specColor;
                            beffect.SpecularPower = specPower;

                            beffect.Alpha = 1.0f;
                        }
                        /*
                        foreach (EffectParameter param in effect.Parameters)
                        {
                            if (param.ParameterType.ToString() == "Texture")
                            {
                                foreach (EffectAnnotation annotation in param.Annotations)
                                {
                                    if (annotation.Name == "ResourceName")
                                    {
                                        String type = param.Annotations["ResourceType"].GetValueString();
                                        String filename = annotation.GetValueString();
                                        Texture texture = null;
                                        if (type == "2D")
                                        {
                                            texture = game.Content.Load<Texture2D>("Textures\\" + filename);
                                        }
                                        else if (type == "Cube")
                                        {
                                            texture = game.Content.Load<TextureCube>("Textures\\" + filename);
                                        }
                                        if (texture != null)
                                        {
                                            param.SetValue(texture);
                                        }
                                    }
                                }
                            }
                        }*/
                        // Specify which effect technique to use.
                        effect.CurrentTechnique = effect.Techniques[0];
                    } 
                    else 
                    {
                        effect = effectMap[effectName];
                    }

                    this.extWorld = transforms[mesh.ParentBone.Index] * this.scaleMat * localWorld;

                    effect.Parameters["world"].SetValue(this.extWorld);
                    effect.Parameters["view"].SetValue(view);
                    effect.Parameters["projection"].SetValue(projection);
                    
                    effect.Parameters["timer"].SetValue(this.timer);

                    foreach (ModelMeshPart meshPart in mesh.MeshParts)
                    {
                        meshPart.Effect = effect;
                    }
                }

                //game.GraphicsDevice.BlendState = BlendState.Opaque;
                //game.GraphicsDevice.DepthStencilState = DepthStencilState.Default;

                mesh.Draw();
            }
        }

        public override void update(GameTime gameTime, Vector3 screenVector)
        {
            base.update(gameTime, screenVector);

            if (this.trailEmitter != null)
            {
                // If this object is a trail-leaver, leave a trail . . .
                this.trailEmitter.Update(gameTime, this.position);
            }
            if (this.destroyed)
            {
                // _Everything_ blows up . . .
                FireAtLocation(this.position, this.velocity);
            }

            this.timer += 0.008f;            
        }

        public void FireAtLocation(Vector3 position, Vector3 velocity)
        {
            // Create fire and smoke particles
            if (!this.exploded)
            {
                for (int i = 0; i < smokeRand.Next(fireParticlesPerFrame); i++)
                {
                    jitter = new Vector3(smokeRand.Next(3000) - 1500, smokeRand.Next(3000) - 1500, smokeRand.Next(3000) - 1500);
                    ((BiplaneGame)game).explosionParticles.AddParticle(this.position + jitter, this.velocity);
                    ((BiplaneGame)game).explosionSmokeParticles.AddParticle(this.position + jitter, this.velocity);
                }
                this.exploded = true;
            }
            for (int i = 0; i < smokeRand.Next(fireParticlesPerFrame); i++)
            {
                jitter = new Vector3(smokeRand.Next(3000) - 1500, smokeRand.Next(3000) - 1500, smokeRand.Next(3000) - 1500);
                ((BiplaneGame)game).fireParticles.AddParticle(this.position + jitter, new Vector3(0.0f, 1000.0f, 0.0f));
                ((BiplaneGame)game).smokePlumeParticles.AddParticle(this.position + jitter, new Vector3(0.0f, smokeRand.Next(2000), 0.0f));
            }
        }
    }
}
