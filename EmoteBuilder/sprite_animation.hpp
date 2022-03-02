#ifndef SPRITE_ANIMATION_HPP
#define SPRITE_ANIMATION_HPP

#include <QObject>
#include <QPixmap>
#include <QTimer>

class SpriteAnimation : public QObject
{
	Q_OBJECT
public:
	SpriteAnimation(QObject* parent = nullptr);
    ~SpriteAnimation();
    void				changeFrame(int frameNumber);
    int                 getCurrentFrameNumber();
    int                 getFrameCount();
    void				init(float fps, QList<QPixmap> frames, int _loopStart = 0);
	bool				isEmpty();
    bool                isPlaying();
    void				play(int fromFrame = 0);
    void				stop(int atFrame = 0);
signals:
    void				frameChanged(QPixmap frame);
	void				frameNumberChanged(int frameNumber);
private slots:
	void				advanceFrame();
private:
    QPixmap 			currentFrame;
	float				fps;
	int					frameNumber;
    QList<QPixmap>      frames;
    QTimer				frameTimer;
	int					loopStart;
};

#endif
