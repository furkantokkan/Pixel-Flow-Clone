namespace PixelFlow.Runtime.Audio
{
    public interface ISoundService
    {
        void PlayClick();
        void PlayPopupOpen();
        void PlayWin();
        void PlayLose();
        void PlayJump();
        void PlayShoot();
        void PlayPigSelect();
        void PlayPop();
    }
}
