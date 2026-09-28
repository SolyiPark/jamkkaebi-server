package com.example.jamkkaebi.artifact.config;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.boot.context.properties.bind.Bindable;
import org.springframework.boot.context.properties.bind.Binder;
import org.springframework.boot.context.properties.source.ConfigurationPropertySources;
import org.springframework.boot.context.properties.source.MapConfigurationPropertySource;
import org.springframework.boot.env.YamlPropertySourceLoader;
import org.springframework.core.env.PropertySource;
import org.springframework.core.io.FileSystemResource;

import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 건물 강화 비용 설정 — 기본값, 덮어쓰기, 음수 스위치가 실제로 설정 파일에서 먹히는지.
 */
class BuildingPropertiesTest {

    @Test
    @DisplayName("설정이 없으면 Lv.2 30 · Lv.3 60 을 쓴다")
    void 기본값() {
        BuildingProperties properties = new BuildingProperties(null);

        assertThat(properties.costFor(2)).isEqualTo(30);
        assertThat(properties.costFor(3)).isEqualTo(60);
    }

    @Test
    @DisplayName("음수로 둔 레벨은 비용 미확정이고, 적지 않은 레벨은 기본값을 유지한다")
    void 음수_스위치() {
        BuildingProperties properties = bind(new MapConfigurationPropertySource(Map.of(
                "app.building.level-up-cost.3", "-1")));

        assertThat(properties.costFor(2)).isEqualTo(30);
        assertThat(properties.costFor(3)).isNull();
    }

    @Test
    @DisplayName("application.yml 의 숫자 키 맵이 레벨별 비용으로 바인딩된다")
    void 설정_파일_바인딩() throws Exception {
        // 클래스패스의 application.yml 은 테스트용 파일이 가리므로 운영 설정 파일을 경로로 읽는다.
        List<PropertySource<?>> yaml = new YamlPropertySourceLoader()
                .load("application", new FileSystemResource("src/main/resources/application.yml"));
        Binder binder = new Binder(ConfigurationPropertySources.from(yaml));

        // 기본값과 섞기 전의 맵을 본다 — 합친 결과로 보면 바인딩이 실패해도 기본값 덕에 통과한다.
        Map<Integer, Integer> bound = binder
                .bind("app.building.level-up-cost", Bindable.mapOf(Integer.class, Integer.class))
                .get();

        assertThat(bound).isEqualTo(Map.of(2, 30, 3, 60));
    }

    private BuildingProperties bind(MapConfigurationPropertySource source) {
        return new Binder(source).bind("app.building", BuildingProperties.class).get();
    }
}
