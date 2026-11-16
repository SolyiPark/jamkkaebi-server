package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.example.jamkkaebi.purification.config.PurificationProperties;
import com.example.jamkkaebi.relic.domain.Difficulty;
import org.springframework.stereotype.Component;

import java.util.List;

/**
 * 난이도 파라미터를 읽는다. 어떤 난이도를 고를 수 있는지는 {@link PlayPlanner} 가 선명도로 정한다.
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
}
