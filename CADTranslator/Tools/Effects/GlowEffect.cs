using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

// �������������������ռ��޸�Ϊ��������Ŀ�ṹһ��
namespace CADTranslator.Tools.Effects
    {
    public class GlowEffect : ShaderEffect
        {
        public static readonly DependencyProperty InputProperty = ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(GlowEffect), 0);
        public static readonly DependencyProperty AmountProperty = DependencyProperty.Register("Amount", typeof(double), typeof(GlowEffect), new UIPropertyMetadata(((double)(1D)), PixelShaderConstantCallback(0)));

        public GlowEffect()
            {
            PixelShader pixelShader = new PixelShader();

            // ����������������URI��ָ������Ŀ�е���ȷ·��
            pixelShader.UriSource = new Uri("/CADTranslator;component/Tools/Effects/Shaders/GlowEffect.ps", UriKind.Relative);
            this.PixelShader = pixelShader;

            this.UpdateShaderValue(InputProperty);
            this.UpdateShaderValue(AmountProperty);
            }

        public Brush Input
            {
            get
                {
                return ((Brush)(this.GetValue(InputProperty)));
                }
            set
                {
                this.SetValue(InputProperty, value);
                }
            }

        public double Amount
            {
            get
                {
                return ((double)(this.GetValue(AmountProperty)));
                }
            set
                {
                this.SetValue(AmountProperty, value);
                }
            }
        }
    }