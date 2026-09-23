package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.example.jamkkaebi.purification.config.PurificationProperties;
import org.springframework.stereotype.Component;

import java.util.List;

/**
 * 난이도 파라미터를 읽고 해금 여부를 판정한다.
 */
@Component
public class DifficultyCatalog {

    private final PurificationProperties properties;

    public DifficultyCatalog(PurificationProperties properties) {
        this.properties = properties;
    }

    public DifficultyParams params(Difficulty difficulty) {
        return properties.paramsOf(difficulty);
    }

    public List<Difficulty> ordered() {
        return properties.ordered();
    }

    // 이 도깨비에게 해금 됐는지. (어려움은 각성+ 에 도달해야 열림)
    public boolean unlocked(Difficulty difficulty, AwakeningStage stage) {
        return params(difficulty).unlockedFor(stage);
    }

    // 해금 전 난이도 플레이 불가능
    public void requireUnlocked(Difficulty difficulty, AwakeningStage stage) {
        if (!unlocked(difficulty, stage)) {
            throw new BusinessException(ErrorCode.DIFFICULTY_LOCKED);
        }
    }

    // 새로 해금된 난이도
    public List<Difficulty> newlyUnlocked(AwakeningStage before, AwakeningStage after) {
        return ordered().stream()
                .filter(difficulty -> !unlocked(difficulty, before) && unlocked(difficulty, after))
                .toList();
    }
}
