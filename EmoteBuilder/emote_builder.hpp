#ifndef EMOTE_BUILDER_HPP
#define EMOTE_BUILDER_HPP

#include <QMainWindow>
#include <QPixmap>
#include "builder.hpp"
#include "sprite_animation.hpp"

QT_BEGIN_NAMESPACE
namespace Ui { class EmoteBuilder; }
QT_END_NAMESPACE

class EmoteBuilder : public QMainWindow
{
    Q_OBJECT

public:
    EmoteBuilder(QWidget *parent = nullptr);
    ~EmoteBuilder();

    static QList<QPoint>            anchors;
    static QMap<QString, QImage>    frames;
    static int                      fps;

private slots:
    void on_loadSpritesButton_clicked();
    void on_buildAtlasButton_clicked();
    void on_playButton_clicked();
    void on_stopButton_clicked();
    void on_prevFrameButton_clicked();
    void on_nextFrameButton_clicked();
    void on_anchorXInput_textChanged(const QString &arg1);
    void on_anchorYInput_textChanged(const QString &arg1);

private:
    void updateFrameDisplay(int frameNumber);
    void updatePixmap(QPixmap pixmap);

    Ui::EmoteBuilder    *ui;
    Builder*            builder;
    SpriteAnimation     currentAnimation;
};
#endif // EMOTE_BUILDER_HPP
